using System.Runtime.InteropServices;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;
using Windows.Graphics.DirectX.Direct3D11;
using WinRT;

namespace HsWin.App.Magnifier;

/// <summary>Free-threaded Windows capture avoids desktop duplication's composition-rate bottleneck.</summary>
internal sealed class WindowsGraphicsCaptureSource : IDisposable
{
    private readonly IDirect3DDevice _device;
    private readonly CaptureActivation<CaptureSession> _activation;
    private readonly GraphicsCaptureItem _item;
    private Action? _request;
    private long _notifications;
    internal long Notifications => Interlocked.Read(ref _notifications);
    internal void SetFrameRequest(Action request) => Volatile.Write(ref _request, request);

    internal WindowsGraphicsCaptureSource(ID3D11Device device, nint monitor, nint window)
    {
        if (!GraphicsCaptureSession.IsSupported()) throw new NotSupportedException("Windows Graphics Capture unavailable.");
        using var dxgi = device.QueryInterface<IDXGIDevice>();
        Marshal.ThrowExceptionForHR(CreateDirect3D11DeviceFromDXGIDevice(dxgi.NativePointer, out var pointer));
        try { _device = MarshalInterface<IDirect3DDevice>.FromAbi(pointer); }
        finally { Marshal.Release(pointer); }
        try
        {
            var interop = GraphicsCaptureItem.As<IGraphicsCaptureItemInterop>();
            var itemId = new Guid("79C3F95B-31F7-4EC2-A464-632EF5D30760");
            var itemPointer = window == 0 ? interop.CreateForMonitor(monitor, itemId) : interop.CreateForWindow(window, itemId);
            try { _item = GraphicsCaptureItem.FromAbi(itemPointer); }
            finally { Marshal.Release(itemPointer); }
            _activation = new CaptureActivation<CaptureSession>(() => new CaptureSession(_device, _item, OnFrameArrived));
        }
        catch { Dispose(); throw; }
    }

    internal void SetActive(bool active)
    {
        _activation.SetActive(active);
    }

    internal bool ConsumeLatest(Func<ID3D11Texture2D, bool> consume)
    {
        var pool = _activation.Current?.Pool;
        if (pool is null) return false;
        // SystemRelativeTime is not frame identity: high-FPS capture can deliver different
        // images with the same timestamp. Consume the newest surface on every notification.
        return LatestCaptureFrameConsumer.ConsumeSurface(pool.TryGetNextFrame, frame =>
        {
            var access = frame.Surface.As<IDirect3DDxgiInterfaceAccess>();
            return new ID3D11Texture2D(access.GetInterface(typeof(ID3D11Texture2D).GUID));
        }, consume);
    }

    public void Dispose()
    {
        Volatile.Write(ref _request, null);
        try { _activation?.Dispose(); }
        finally { _device?.Dispose(); }
    }

    private void OnFrameArrived(Direct3D11CaptureFramePool sender, object args)
    {
        Interlocked.Increment(ref _notifications);
        Volatile.Read(ref _request)?.Invoke();
    }

    private sealed class CaptureSession : IDisposable
    {
        internal Direct3D11CaptureFramePool Pool { get; }
        private readonly GraphicsCaptureSession _session = null!;
        private readonly global::Windows.Foundation.TypedEventHandler<Direct3D11CaptureFramePool, object> _arrived;

        internal CaptureSession(IDirect3DDevice device, GraphicsCaptureItem item,
            global::Windows.Foundation.TypedEventHandler<Direct3D11CaptureFramePool, object> arrived)
        {
            _arrived = arrived;
            Pool = Direct3D11CaptureFramePool.CreateFreeThreaded(device, DirectXPixelFormat.B8G8R8A8UIntNormalized, 3, item.Size);
            try
            {
                _session = Pool.CreateCaptureSession(item);
                GraphicsCaptureSessionOptions.Apply(_session);
                Pool.FrameArrived += _arrived;
                _session.StartCapture();
            }
            catch { Dispose(); throw; }
        }

        public void Dispose()
        {
            Pool.FrameArrived -= _arrived;
            try { _session?.Dispose(); }
            finally { Pool.Dispose(); }
        }
    }

    [DllImport("d3d11.dll", ExactSpelling = true)]
    private static extern int CreateDirect3D11DeviceFromDXGIDevice(nint device, out nint graphicsDevice);

    [ComImport, Guid("3628E81B-3CAC-4C60-B7F4-23CE0E0C3356"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IGraphicsCaptureItemInterop
    {
        nint CreateForWindow(nint window, in Guid iid);
        nint CreateForMonitor(nint monitor, in Guid iid);
    }

    [ComImport, Guid("A9B3D012-3DF2-4EE3-B8D1-8695F457D3C1"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IDirect3DDxgiInterfaceAccess
    {
        nint GetInterface(in Guid iid);
    }
}
