namespace HsWin.App.Magnifier;
using HsWin.App.Native;

internal interface IGpuMagnifierRenderer : IDisposable
{
    long AcquiredFrames { get; }
    string Backend => "GPU";
    MagnifierGpuDiagnostics Diagnostics => default;
    void SetFrameRequest(Action request, int fps) { }
    void SetActive(bool active) { }
    void Configure(MagnifierGeometry geometry, NativeRect monitor, int border);
    bool Render();
}
