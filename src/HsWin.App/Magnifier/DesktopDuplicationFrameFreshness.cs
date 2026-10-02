namespace HsWin.App.Magnifier;

/// <summary>DXGI LastPresentTime distinguishes desktop image updates from cursor-only events.</summary>
internal sealed class DesktopDuplicationFrameFreshness
{
    private long _timestamp;
    internal bool Accept(long timestamp)
    {
        if (timestamp <= _timestamp) return false;
        _timestamp = timestamp;
        return true;
    }
}
