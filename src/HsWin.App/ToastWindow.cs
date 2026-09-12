using HsWin.Core.Alerts;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using MediaBrush = System.Windows.Media.Brush;
using WpfBrushes = System.Windows.Media.Brushes;
using WpfColor = System.Windows.Media.Color;
using WpfOrientation = System.Windows.Controls.Orientation;

namespace HsWin.App;

internal interface IToastView
{
    double ActualHeight { get; }

    double ActualWidth { get; }

    bool IsVisible { get; }

    double Left { get; set; }

    double Top { get; set; }

    Visual PlacementVisual { get; }

    void Close();

    void Hide();

    void Show();

    void UpdateLayout();

    void UpdateRequest(AlertRequest request);

    void BeginEnterAnimation();

    void BeginExitAnimation(Action onComplete);

    void CancelExitAnimation();

    void PrepareForShow();
}

internal sealed class ToastWindow : Window, IToastView
{
    private static readonly MediaBrush WhiteBrush = WpfBrushes.White;
    private static readonly MediaBrush TextBrush = WpfBrushes.Black;
    private static readonly MediaBrush ErrorBrush = CreateFrozenBrush(WpfColor.FromRgb(242, 20, 26));
    private static readonly MediaBrush SuccessBrush = CreateFrozenBrush(WpfColor.FromRgb(22, 163, 74));
    private static readonly Transform DotTransform = CreateFrozenTransform(0, ToastStyleMetrics.DotTranslateY);
    private static readonly DropShadowEffect PillShadow = CreatePillShadow();
    private static readonly Geometry LoaderGeometry = CreateLoaderGeometry();

    private readonly BlurEffect _exitBlur;
    private readonly UIElement _exitTarget;
    private readonly ToastExitAnimator _exitAnimator;
    private readonly PillBorder _border;
    private readonly Grid _iconSlot;
    private readonly Ellipse _dot;
    private readonly Viewbox _loader;
    private readonly RotateTransform _loaderRotation;
    private readonly TextBlock _text;
    private readonly ScrittoTextPanel _scritto;
    private bool _scrittoLive;
    private bool _loaderSpinning;

    public ToastWindow()
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = WpfBrushes.Transparent;
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.WidthAndHeight;
        ShowActivated = false;
        ShowInTaskbar = false;
        Topmost = true;
        Focusable = false;
        UseLayoutRounding = true;
        SnapsToDevicePixels = true;

        _dot = CreateDot();
        _loaderRotation = new RotateTransform();
        _loader = CreateLoaderIcon(_loaderRotation);
        _iconSlot = CreateIconSlot(_dot, _loader);
        _text = CreateText();
        _scritto = new ScrittoTextPanel
        {
            VerticalAlignment = VerticalAlignment.Center,
            Visibility = Visibility.Collapsed,
        };
        TextElement.SetFontFamily(_scritto, ToastFonts.RegularFontFamily);
        TextElement.SetFontSize(_scritto, ToastStyleMetrics.TextFontSize);
        TextElement.SetFontWeight(_scritto, ToastStyleMetrics.TextFontWeight);
        _scritto.SetValue(TextElement.ForegroundProperty, TextBrush);
        _scritto.WidthRollStarted += OnScrittoWidthRollStarted;

        var panel = new StackPanel
        {
            Orientation = WpfOrientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center
        };
        panel.Children.Add(_iconSlot);
        panel.Children.Add(_text);
        panel.Children.Add(_scritto);

        _border = new PillBorder
        {
            Background = WhiteBrush,
            Effect = PillShadow,
            Child = panel
        };

        _exitBlur = new BlurEffect { Radius = 0 };
        _exitTarget = new Border { Effect = _exitBlur, Child = _border };
        _exitAnimator = new ToastExitAnimator(_exitTarget, _exitBlur);

        var inset = ToastStyleMetrics.ShadowInset;
        Content = new Grid
        {
            Margin = new Thickness(inset),
            Children = { _exitTarget }
        };
    }

    public Visual PlacementVisual => this;

    public void BeginEnterAnimation()
    {
    }

    public void BeginExitAnimation(Action onComplete) => _exitAnimator.Begin(onComplete);

    public void CancelExitAnimation() => _exitAnimator.Cancel();

    public void PrepareForShow() => _exitAnimator.PrepareForShow();

    public void UpdateRequest(AlertRequest request)
    {
        ApplyIconState(request);

        if (request.AnimateRoll)
        {
            _text.Visibility = Visibility.Collapsed;
            _scritto.Visibility = Visibility.Visible;
            if (!_scrittoLive)
            {
                _scritto.Snap(request.Text);
                _scrittoLive = true;
            }
            else
            {
                RollOrSnap(request);
            }

            return;
        }

        _scritto.Visibility = Visibility.Collapsed;
        _text.Visibility = Visibility.Visible;
        _text.Text = request.Text;
        _scrittoLive = false;
    }

    /// <summary>
    /// Rolls the panel, falling back to an instant set if the roll cannot
    /// run: eye candy must never take down the toast path on the dispatcher.
    /// </summary>
    private void RollOrSnap(AlertRequest request)
    {
        try
        {
            _scritto.Roll(request.Text, request.Trend, request.RollDurationMs, request.Bounce);
        }
        catch (Exception)
        {
            _scritto.Snap(request.Text);
        }
    }

    /// <summary>
    /// Rides the window's left edge while the roll animates the pill width,
    /// so a centered toast stays centered instead of growing rightward.
    /// </summary>
    private void OnScrittoWidthRollStarted(double oldWidth, double newWidth, TimeSpan duration)
    {
        var from = Left;
        if (double.IsNaN(from))
        {
            return;
        }

        var to = from - (newWidth - oldWidth) / 2;
        var animation = new DoubleAnimation(from, to, duration)
        {
            EasingFunction = new ScrittoShrinkEasing(),
            FillBehavior = FillBehavior.Stop,
        };
        BeginAnimation(LeftProperty, animation);
        Left = to;
    }

    private void ApplyIconState(AlertRequest request)
    {
        var icon = request.EffectiveIcon;

        if (icon is AlertIcon.None)
        {
            StopLoaderSpin();
            _iconSlot.Visibility = Visibility.Collapsed;
            _border.Padding = new Thickness(
                ToastStyleMetrics.NormalHorizontalPadding,
                ToastStyleMetrics.VerticalPadding,
                ToastStyleMetrics.NormalHorizontalPadding,
                ToastStyleMetrics.VerticalPadding);
            return;
        }

        _iconSlot.Visibility = Visibility.Visible;
        _dot.Visibility = icon is AlertIcon.Dot ? Visibility.Visible : Visibility.Collapsed;
        _loader.Visibility = icon is AlertIcon.Loader ? Visibility.Visible : Visibility.Collapsed;

        if (icon is AlertIcon.Loader)
        {
            StartLoaderSpin();
        }
        else
        {
            StopLoaderSpin();
            _dot.Fill = request.Kind is AlertKind.Error ? ErrorBrush : SuccessBrush;
        }

        _border.Padding = new Thickness(
            ToastStyleMetrics.IconStateLeftPadding,
            ToastStyleMetrics.VerticalPadding,
            ToastStyleMetrics.DotStateRightPadding,
            ToastStyleMetrics.VerticalPadding);
    }

    private void StartLoaderSpin()
    {
        if (_loaderSpinning)
        {
            return;
        }

        _loaderSpinning = true;
        var animation = new DoubleAnimation(0, 360, TimeSpan.FromMilliseconds(ToastStyleMetrics.LoaderSpinDurationMs))
        {
            RepeatBehavior = RepeatBehavior.Forever
        };
        _loaderRotation.BeginAnimation(RotateTransform.AngleProperty, animation);
    }

    private void StopLoaderSpin()
    {
        if (!_loaderSpinning)
        {
            return;
        }

        _loaderRotation.BeginAnimation(RotateTransform.AngleProperty, null);
        _loaderRotation.Angle = 0;
        _loaderSpinning = false;
    }

    private static Grid CreateIconSlot(Ellipse dot, Viewbox loader)
    {
        return new()
        {
            Width = ToastStyleMetrics.IconSlotSize,
            Height = ToastStyleMetrics.IconSlotSize,
            Margin = new Thickness(0, 0, ToastStyleMetrics.IconTextGap, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Visibility = Visibility.Collapsed,
            Children = { dot, loader }
        };
    }

    private static Ellipse CreateDot()
    {
        return new()
        {
            Width = ToastStyleMetrics.DotSize,
            Height = ToastStyleMetrics.DotSize,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            RenderTransform = DotTransform,
            Visibility = Visibility.Collapsed
        };
    }

    private static Viewbox CreateLoaderIcon(RotateTransform rotation)
    {
        var path = new Path
        {
            Data = LoaderGeometry,
            Stroke = TextBrush,
            StrokeThickness = 2,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            StrokeLineJoin = PenLineJoin.Round,
            Fill = null
        };

        var canvas = new Canvas
        {
            Width = 24,
            Height = 24,
            Children = { path }
        };

        return new()
        {
            Width = ToastStyleMetrics.LoaderIconSize,
            Height = ToastStyleMetrics.LoaderIconSize,
            Child = canvas,
            RenderTransform = rotation,
            RenderTransformOrigin = new System.Windows.Point(0.5, 0.5),
            Stretch = Stretch.Uniform,
            Visibility = Visibility.Collapsed
        };
    }

    private static TextBlock CreateText()
    {
        return new()
        {
            FontFamily = ToastFonts.RegularFontFamily,
            FontSize = ToastStyleMetrics.TextFontSize,
            FontWeight = ToastStyleMetrics.TextFontWeight,
            Foreground = TextBrush,
            MaxWidth = ToastStyleMetrics.TextMaxWidth,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center
        };
    }

    private static MediaBrush CreateFrozenBrush(WpfColor color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    private static Transform CreateFrozenTransform(double x, double y)
    {
        var transform = new TranslateTransform(x, y);
        transform.Freeze();
        return transform;
    }

    private static Geometry CreateLoaderGeometry()
    {
        var geometry = Geometry.Parse(
            "M 12 2 L 12 6 " +
            "M 16.2 7.8 L 19.1 4.9 " +
            "M 18 12 L 22 12 " +
            "M 16.2 16.2 L 19.1 19.1 " +
            "M 12 18 L 12 22 " +
            "M 4.9 19.1 L 7.8 16.2 " +
            "M 2 12 L 6 12 " +
            "M 4.9 4.9 L 7.8 7.8");
        geometry.Freeze();
        return geometry;
    }

    private static DropShadowEffect CreatePillShadow()
    {
        var effect = new DropShadowEffect
        {
            Color = WpfColor.FromRgb(0, 0, 0),
            Opacity = ToastStyleMetrics.ShadowOpacity,
            BlurRadius = ToastStyleMetrics.ShadowBlurRadius,
            ShadowDepth = ToastStyleMetrics.ShadowDepth,
            Direction = ToastStyleMetrics.ShadowDirection,
            RenderingBias = RenderingBias.Quality,
        };
        effect.Freeze();
        return effect;
    }

    private sealed class PillBorder : Border
    {
        protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
        {
            base.OnRenderSizeChanged(sizeInfo);

            var radius = Math.Max(0, ActualHeight / 2);
            CornerRadius = new CornerRadius(radius);
        }
    }
}
