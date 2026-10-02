using System.Runtime.InteropServices;
using Windows.Foundation.Metadata;
using Windows.Graphics.Capture;
using WinRT;

namespace HsWin.App.Magnifier;

/// <summary>Optional newer WinRT properties without raising the app's minimum supported Windows SDK.</summary>
internal static class GraphicsCaptureSessionOptions
{
    // These interfaces each extend IInspectable with a getter and setter (vtable slots 6 and 7).
    private static readonly Guid IntervalInterface = new("67C0EA62-1F85-5061-925A-239BE0AC09CB");
    private static readonly Guid CursorInterface = new("2C39AE40-7D2E-5044-804E-8B6799D4CF9E");

    internal static void Apply(GraphicsCaptureSession session)
    {
        SetZeroProperty(session, "MinUpdateInterval", IntervalInterface, false);
        SetZeroProperty(session, "IsCursorCaptureEnabled", CursorInterface, true);
    }

    private static unsafe void SetZeroProperty(GraphicsCaptureSession session, string name, Guid iid, bool isBoolean)
    {
        if (!ApiInformation.IsPropertyPresent("Windows.Graphics.Capture.GraphicsCaptureSession", name)) return;
        var identity = ((IWinRTObject)session).NativeObject.ThisPtr;
        Marshal.ThrowExceptionForHR(Marshal.QueryInterface(identity, in iid, out var pointer));
        try
        {
            var setter = (*(nint**)pointer)[7];
            // Zero interval allows the capture source to run at display cadence; the host caps fps.
            var result = isBoolean
                ? ((delegate* unmanaged[Stdcall]<nint, byte, int>)setter)(pointer, 0)
                : ((delegate* unmanaged[Stdcall]<nint, long, int>)setter)(pointer, 0);
            Marshal.ThrowExceptionForHR(result);
        }
        finally { Marshal.Release(pointer); }
    }
}
