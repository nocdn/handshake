namespace HsWin.Core.Magnifier;

public enum MagnifierScreen
{
    Foreground,
    Primary,
    Mouse
}

public sealed record MagnifierOptions(double Zoom = 2, int Diameter = 720, int BorderWidth = 3,
    MagnifierScreen Screen = MagnifierScreen.Foreground, int Fps = 240)
{
    public const double MinZoom = 1;
    public const double MaxZoom = 16;
    public const int MinDiameter = 80;
    public const int MaxDiameter = 1600;
    public const int MinBorderWidth = 0;
    public const int MaxBorderWidth = 16;
    public const int MinFps = 30;
    public const int MaxFps = 360;

    public static MagnifierOptions Defaults { get; } = new();

    public static void ValidateZoom(double zoom)
    {
        if (!double.IsFinite(zoom) || zoom < MinZoom || zoom > MaxZoom)
        {
            throw new ArgumentException($"Magnifier zoom must be a finite number from {MinZoom} to {MaxZoom}.");
        }
    }
}
