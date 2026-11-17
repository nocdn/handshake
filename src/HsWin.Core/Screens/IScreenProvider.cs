using HsWin.Core.Geometry;

namespace HsWin.Core.Screens;

public sealed record ScreenSnapshot(string Id, string Name, bool IsPrimary, PixelRectangle Bounds, PixelRectangle WorkingArea);

public interface IScreenProvider
{
    IReadOnlyList<ScreenSnapshot> AllScreens();
}

public sealed class NullScreenProvider : IScreenProvider
{
    public static NullScreenProvider Instance { get; } = new();
    public IReadOnlyList<ScreenSnapshot> AllScreens() => [];
}
