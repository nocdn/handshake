using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using System.Runtime.InteropServices;
using static Vortice.Direct3D11.D3D11;
using static Vortice.DXGI.DXGI;
using HsWin.App.Native;

namespace HsWin.App.Magnifier;

/// <summary>Copies desktop frames entirely on the GPU, with a nonblocking acquire.</summary>
internal sealed class DesktopTextureCapture : IDisposable
{
    private readonly List<IDisposable> _resources = [];
    private readonly IDXGIOutputDuplication _duplication = null!;
    private readonly WindowsGraphicsCaptureSource? _windowsCapture;
    internal string Backend { get; private set; } = "desktop duplication";
    private ID3D11Texture2D? _frame;
    private Vortice.Mathematics.Box _region;
    private readonly DesktopDuplicationFrameFreshness _freshness = new();
    private bool _ownsFrame;
    internal ID3D11Device Device { get; private set; } = null!;
    internal ID3D11DeviceContext Context { get; private set; } = null!;
    internal ID3D11Texture2D Texture { get; private set; } = null!;
    internal ID3D11ShaderResourceView View { get; private set; } = null!;
    internal bool HasFrame { get; private set; }
    internal long AcquiredFrames { get; private set; }
    internal long MissedFrames { get; private set; }
    internal long Notifications => _windowsCapture?.Notifications ?? AcquiredFrames;
    internal void SetFrameRequest(Action request) => _windowsCapture?.SetFrameRequest(request);
    internal void SetActive(bool active)
    {
        if (!active) ReleaseFrame();
        _windowsCapture?.SetActive(active);
    }

    internal DesktopTextureCapture(nint monitor, nint window)
    {
        try
        {
            using var factory = CreateDXGIFactory1<IDXGIFactory1>();
            for (uint adapterIndex = 0; factory.EnumAdapters1(adapterIndex, out var adapter).Success; adapterIndex++)
            {
                using (adapter)
                {
                    for (uint outputIndex = 0; adapter.EnumOutputs(outputIndex, out var output).Success; outputIndex++)
                    {
                        using (output)
                        {
                            if (output.Description.Monitor != monitor) continue;
                            using var output1 = output.QueryInterface<IDXGIOutput1>();
                            D3D11CreateDevice(adapter, DriverType.Unknown, DeviceCreationFlags.BgraSupport,
                                [FeatureLevel.Level_11_0], out ID3D11Device device, out ID3D11DeviceContext context).CheckError();
                            Device = Own(device);
                            Context = Own(context);
                            try
                            {
                                _windowsCapture = Own(new WindowsGraphicsCaptureSource(Device, monitor, window));
                                Backend = window == 0 ? "Windows Graphics Capture monitor" : $"Windows Graphics Capture fullscreen window 0x{window:X}";
                                return;
                            }
                            catch (Exception exception) when (exception is COMException or NotSupportedException or InvalidCastException)
                            {
                                Backend = $"desktop duplication (Windows capture unavailable: {exception.Message})";
                            }
                            _duplication = Own(output1.DuplicateOutput(Device));
                            var mode = _duplication.Description;
                            if (mode.Rotation != ModeRotation.Identity && mode.Rotation != ModeRotation.Unspecified)
                                throw new NotSupportedException("Rotated displays use the native magnifier fallback.");
                            return;
                        }
                    }
                }
            }
            throw new InvalidOperationException("No graphics output matches the magnifier screen.");
        }
        catch { Dispose(); throw; }
    }

    internal void Configure(NativeRect source, NativeRect monitor)
    {
        var region = MagnifierCaptureRegion.Create(source, monitor);
        if (_region.Equals(region) && Texture is not null) return;
        Context.PSSetShaderResource(0, null!);
        View?.Dispose();
        Texture?.Dispose();
        _region = region;
        Texture = Device.CreateTexture2D(new Texture2DDescription
        {
            Width = (uint)(source.Right - source.Left), Height = (uint)(source.Bottom - source.Top),
            MipLevels = 1, ArraySize = 1, Format = Format.B8G8R8A8_UNorm,
            SampleDescription = new SampleDescription(1, 0), BindFlags = BindFlags.ShaderResource
        });
        View = Device.CreateShaderResourceView(Texture);
        HasFrame = _frame is not null;
        if (HasFrame) CopyRegion(_frame!);
    }

    internal bool Refresh()
    {
        if (_windowsCapture is not null)
        {
            return _windowsCapture.ConsumeLatest(CopyWindowsFrame);
        }
        // Keep the duplication surface owned between ticks. Releasing immediately after a copy
        // makes Windows copy every intervening desktop update, including regions outside the lens.
        ReleaseFrame();
        var result = _duplication.AcquireNextFrame(0, out var info, out var resource);
        if (result.Code == MagnifierNative.DxgiResult.WaitTimeout) return false; // keep the latest image.
        result.CheckError();
        _ownsFrame = true;
        using (resource)
        {
            _frame = resource.QueryInterface<ID3D11Texture2D>();
            // Cursor-only events and repeated desktop timestamps are not fresh image frames.
            if (!_freshness.Accept(info.LastPresentTime)) return false;
            if (Texture is not null)
            {
                CopyRegion(_frame);
                HasFrame = true;
                AcquiredFrames++;
                MissedFrames += Math.Max(0, (long)info.AccumulatedFrames - 1);
            }
        }
        return true;
    }

    private bool CopyWindowsFrame(ID3D11Texture2D texture)
    {
        CopyRegion(texture);
        Context.Flush(); // Submit the GPU copy before returning the capture-pool surface.
        HasFrame = true;
        AcquiredFrames++;
        return true;
    }

    private void CopyRegion(ID3D11Texture2D frame)
    {
        Context.PSSetShaderResource(0, null!);
        Context.CopySubresourceRegion(Texture, 0, 0, 0, 0, frame, 0, _region);
    }

    private void ReleaseFrame()
    {
        _frame?.Dispose();
        _frame = null;
        if (!_ownsFrame) return;
        _ownsFrame = false;
        _duplication.ReleaseFrame().CheckError();
    }

    private T Own<T>(T resource) where T : IDisposable { _resources.Add(resource); return resource; }

    public void Dispose()
    {
        // Access may already be lost during a display/game mode transition; cleanup still proceeds.
        try { ReleaseFrame(); } catch (SharpGen.Runtime.SharpGenException) { }
        View?.Dispose();
        Texture?.Dispose();
        for (var i = _resources.Count - 1; i >= 0; i--) _resources[i].Dispose();
        _resources.Clear();
    }
}
