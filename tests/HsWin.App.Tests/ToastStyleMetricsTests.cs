namespace HsWin.App.Tests;

public sealed class ToastStyleMetricsTests
{
    [Fact]
    public void ToastTypographyUsesRequestedFontSize()
    {
        Assert.Equal(14, ToastStyleMetrics.TextFontSize);
        Assert.Equal(520, ToastStyleMetrics.TextMaxWidth);
    }

    [Fact]
    public void ToastTypographyUsesSofterFontWeight()
    {
        Assert.Equal(400, ToastStyleMetrics.TextFontWeight.ToOpenTypeWeight());
    }

    [Fact]
    public void ToastDotUsesRequestedSizeAndGap()
    {
        Assert.Equal(6, ToastStyleMetrics.DotSize);
        Assert.Equal(-0.75, ToastStyleMetrics.DotTranslateY);
        Assert.Equal(14, ToastStyleMetrics.IconSlotSize);
        Assert.Equal(14, ToastStyleMetrics.LoaderIconSize);
        Assert.Equal(6, ToastStyleMetrics.IconTextGap);
        Assert.Equal(900, ToastStyleMetrics.LoaderSpinDurationMs);
    }

    [Fact]
    public void ToastPaddingPreservesRobinInspiredInsets()
    {
        Assert.Equal(13, ToastStyleMetrics.IconStateLeftPadding);
        Assert.Equal(18, ToastStyleMetrics.DotStateRightPadding);
        Assert.Equal(17, ToastStyleMetrics.NormalHorizontalPadding);
        Assert.Equal(11, ToastStyleMetrics.VerticalPadding);
    }

    [Fact]
    public void ToastShadowUsesSoftBlurWithSmallDepth()
    {
        Assert.Equal(42, ToastStyleMetrics.ShadowBlurRadius);
        Assert.Equal(1, ToastStyleMetrics.ShadowDepth);
        Assert.Equal(270, ToastStyleMetrics.ShadowDirection);
        Assert.Equal(0.22, ToastStyleMetrics.ShadowOpacity);
    }

    [Fact]
    public void ToastShadowInsetFitsBlurAndDepth()
    {
        Assert.Equal(39, ToastStyleMetrics.ShadowInset);
    }

    [Fact]
    public void ToastExitAnimationUsesQuickEaseInFriendlyTiming()
    {
        Assert.Equal(140, ToastStyleMetrics.ExitDurationMs);
        Assert.Equal(4, ToastStyleMetrics.ExitBlurRadius);
    }
}
