using Vortice.D3DCompiler;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DirectComposition;
using Vortice.DXGI;
using Microsoft.Win32.SafeHandles;
using static Vortice.DirectComposition.DComp;
using static Vortice.DXGI.DXGI;
using HsWin.App.Native;

namespace HsWin.App.Magnifier;

/// <summary>GPU scaling and a composition swap chain: no screen readback or CPU bitmap copies.</summary>
internal sealed class GpuMagnifierRenderer : IGpuMagnifierRenderer
{
    private static readonly Lazy<ReadOnlyMemory<byte>> VertexCode = new(() =>
        Compiler.Compile(MagnifierShader.Source, "VS", "hswin-magnifier", "vs_5_0", ShaderFlags.OptimizationLevel3));
    private static readonly Lazy<ReadOnlyMemory<byte>> PixelCode = new(() =>
        Compiler.Compile(MagnifierShader.Source, "PS", "hswin-magnifier", "ps_5_0", ShaderFlags.OptimizationLevel3));
    private readonly List<IDisposable> _resources = [];
    private readonly DesktopTextureCapture _capture;
    private readonly IDXGISwapChain1 _swapChain = null!;
    private readonly IDCompositionDevice _composition = null!;
    private readonly IDCompositionTarget _target = null!;
    private readonly ID3D11Buffer _settings = null!;
    private readonly WaitHandle _frameAvailable = null!;
    private ID3D11RenderTargetView? _renderTarget;
    private int _diameter;
    private bool _disposed;
    private MagnifierFramePump _frames = new(240);
    private readonly Func<bool> _captureLatest;
    private readonly Func<bool> _presentLatest;
    private long _queueSignalMisses, _presentRejected, _captureCalls, _captureTicks, _presentCalls, _presentTicks;
    public MagnifierGpuDiagnostics Diagnostics => new(_capture.Notifications, _frames.Requests, _frames.RateLimited,
        _queueSignalMisses, _presentRejected, _captureCalls, _captureTicks, _presentCalls, _presentTicks);

    public long AcquiredFrames => _capture.AcquiredFrames;
    public string Backend => _capture.Backend;
    public void SetActive(bool active) { _capture.SetActive(active); _frames.ResetClock(); }
    public void SetFrameRequest(Action request, int fps)
    {
        _frames = new MagnifierFramePump(fps);
        _capture.SetFrameRequest(request);
    }

    internal GpuMagnifierRenderer(nint host, nint monitor, nint window, int diameter)
    {
        _captureLatest = CaptureLatest;
        _presentLatest = PresentLatest;
        _capture = new DesktopTextureCapture(monitor, window);
        try
        {
            using var factory = CreateDXGIFactory1<IDXGIFactory2>();
            _swapChain = Own(factory.CreateSwapChainForComposition(_capture.Device, new SwapChainDescription1
            {
                Width = (uint)diameter, Height = (uint)diameter, Format = Format.B8G8R8A8_UNorm,
                SampleDescription = new SampleDescription(1, 0), BufferUsage = Usage.RenderTargetOutput,
                BufferCount = 3, SwapEffect = SwapEffect.FlipSequential, Scaling = Scaling.Stretch,
                AlphaMode = AlphaMode.Premultiplied, Flags = SwapChainFlags.FrameLatencyWaitableObject
            }));
            var swapChain2 = Own(_swapChain.QueryInterface<IDXGISwapChain2>());
            // Two queued frames tolerate an unsynchronized capture/display clock without
            // the one-frame queue forcing every other refresh to be skipped under GPU load.
            swapChain2.MaximumFrameLatency = 2;
            var frameHandle = swapChain2.FrameLatencyWaitableObject;
            MagnifierNative.Check(frameHandle != 0, "create GPU frame availability handle");
            _frameAvailable = Own(new FrameAvailabilityHandle(frameHandle));
            using var dxgiDevice = _capture.Device.QueryInterface<IDXGIDevice1>();
            dxgiDevice.SetMaximumFrameLatency(2).CheckError();
            _composition = Own(DCompositionCreateDevice<IDCompositionDevice>(dxgiDevice));
            _composition.CreateTargetForHwnd(host, true, out var target).CheckError();
            _target = Own(target);
            _composition.CreateVisual(out var visual).CheckError();
            Own(visual);
            visual.SetContent(_swapChain).CheckError();
            _target.SetRoot(visual).CheckError();
            _composition.Commit().CheckError();

            var device = _capture.Device;
            var context = _capture.Context;
            var vertex = Own(device.CreateVertexShader(VertexCode.Value.Span));
            var pixel = Own(device.CreatePixelShader(PixelCode.Value.Span));
            var sampler = Own(device.CreateSamplerState(new SamplerDescription
            {
                Filter = Filter.MinMagMipLinear, AddressU = TextureAddressMode.Clamp,
                AddressV = TextureAddressMode.Clamp, AddressW = TextureAddressMode.Clamp,
                MaxLOD = float.MaxValue
            }));
            _settings = Own(device.CreateBuffer(32, BindFlags.ConstantBuffer, ResourceUsage.Default));
            context.IASetPrimitiveTopology(PrimitiveTopology.TriangleList);
            context.VSSetShader(vertex);
            context.PSSetShader(pixel);
            context.PSSetSampler(0, sampler);
            context.PSSetConstantBuffer(0, _settings);
            _diameter = diameter;
            CreateRenderTarget();
        }
        catch { Dispose(); throw; }
    }

    public void Configure(MagnifierGeometry geometry, NativeRect monitor, int border)
    {
        if (_diameter != geometry.Diameter)
        {
            _capture.Context.OMSetRenderTargets((ID3D11RenderTargetView)null!);
            _renderTarget?.Dispose();
            _renderTarget = null;
            _swapChain.ResizeBuffers(3, (uint)geometry.Diameter, (uint)geometry.Diameter, Format.B8G8R8A8_UNorm,
                SwapChainFlags.FrameLatencyWaitableObject).CheckError();
            _diameter = geometry.Diameter;
            CreateRenderTarget();
        }
        // Cache the lens's 1x footprint, so setZoom can resample the last image even on a static
        // desktop. Capture-pool buffers can then be returned immediately after each GPU copy.
        var footprint = MagnifierGeometry.Calculate(monitor, geometry.Diameter, border, 1).Source;
        _capture.Configure(footprint, monitor);
        var settings = MagnifierShaderParameters.Create(geometry, footprint, border);
        _capture.Context.UpdateSubresource(settings, _settings);
        _frames.SetPendingImage(_capture.HasFrame);
    }

    private void CreateRenderTarget()
    {
        using var buffer = _swapChain.GetBuffer<ID3D11Texture2D>(0);
        _renderTarget = _capture.Device.CreateRenderTargetView(buffer);
    }

    public bool Render()
        => _frames.Render(System.Diagnostics.Stopwatch.GetTimestamp(), _captureLatest, _presentLatest);

    private bool CaptureLatest()
    {
        var now = System.Diagnostics.Stopwatch.GetTimestamp();
        try { return _capture.Refresh(); }
        finally
        {
            _captureCalls++;
            _captureTicks += System.Diagnostics.Stopwatch.GetTimestamp() - now;
        }
    }

    private bool PresentLatest()
    {
        if (!_capture.HasFrame) return false;
        // The waitable signal is advisory. Present(0) can replace queued images even when
        // it is unsignaled; DoNotWait is the authoritative, nonblocking capacity check.
        if (!_frameAvailable.WaitOne(0)) { _queueSignalMisses++; }
        var context = _capture.Context;
        context.OMSetRenderTargets(_renderTarget!);
        context.RSSetViewport(0, 0, _diameter, _diameter);
        context.PSSetShaderResource(0, _capture.View);
        context.Draw(3, 0);
        // Zero sync interval lets DXGI replace queued frames with the newest image.
        var presentStart = System.Diagnostics.Stopwatch.GetTimestamp();
        var present = _swapChain.Present(0, PresentFlags.DoNotWait);
        _presentCalls++;
        _presentTicks += System.Diagnostics.Stopwatch.GetTimestamp() - presentStart;
        if (present.Code == MagnifierNative.DxgiResult.WasStillDrawing)
        {
            _presentRejected++;
            context.Flush();
            return false; // GPU queue full: drop instead of waiting.
        }
        present.CheckError();
        return true;
    }

    private T Own<T>(T resource) where T : IDisposable { _resources.Add(resource); return resource; }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        // A lost graphics device makes these calls throw. Every step still runs so the native
        // resources are released, and the failures are reported together afterwards.
        var failures = new List<Exception>();
        void Run(Action step)
        {
            try { step(); }
            catch (Exception exception) { failures.Add(exception); }
        }

        Run(() =>
        {
            _capture.Context?.ClearState();
            if (_target is not null)
            {
                _target.SetRoot(null!);
                _composition.Commit();
            }
        });
        Run(() => _renderTarget?.Dispose());
        _renderTarget = null;
        for (var i = _resources.Count - 1; i >= 0; i--)
        {
            var resource = _resources[i];
            Run(resource.Dispose);
        }

        _resources.Clear();
        Run(_capture.Dispose);
        if (failures.Count > 0) throw new AggregateException("Magnifier GPU renderer cleanup failed.", failures);
    }

    private sealed class FrameAvailabilityHandle : WaitHandle
    {
        internal FrameAvailabilityHandle(nint handle) => SafeWaitHandle = new SafeWaitHandle(handle, ownsHandle: true);
    }
}
