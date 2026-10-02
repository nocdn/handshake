namespace HsWin.App;

using System.Windows;

internal static class ToastStyleMetrics
{
    public static readonly FontWeight TextFontWeight = FontWeights.Normal;

    public const double TextFontSize = 14;
    public const double TextMaxWidth = 520;
    public const double DotSize = 6;
    public const double DotStateRightPadding = 18;
    public const double DotTranslateY = -0.75;
    public const double IconSlotSize = 14;
    public const double IconTextGap = 6;
    public const double IconStateLeftPadding = 13;
    public const double LoaderIconSize = 14;
    public const int LoaderSpinDurationMs = 900;
    public const double NormalHorizontalPadding = 17;
    public const double VerticalPadding = 11;

    /// <summary>Soft blur radius for the pill drop shadow (device-independent pixels).</summary>
    public const double ShadowBlurRadius = 42;

    /// <summary>Small downward offset so the shadow reads as ambient depth.</summary>
    public const double ShadowDepth = 1;

    /// <summary>Shadow cast angle in degrees (270 = straight down).</summary>
    public const double ShadowDirection = 270;

    public const double ShadowOpacity = 0.22;

    /// <summary>
    /// Inset around the pill so <see cref="System.Windows.Media.Effects.DropShadowEffect"/>
    /// is not clipped by the transparent window bounds.
    /// </summary>
    public static double ShadowInset =>
        Math.Ceiling(ShadowBlurRadius * 0.88 + ShadowDepth) + 1;

    /// <summary>Exit fade/blur duration (milliseconds). Kept short for dismissals.</summary>
    public const int ExitDurationMs = 140;

    /// <summary>Peak <see cref="System.Windows.Media.Effects.BlurEffect.Radius"/> during exit.</summary>
    public const double ExitBlurRadius = 4;
}
