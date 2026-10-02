using HsWin.App.Native;
using HsWin.Core.Alerts;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using MediaBrush = System.Windows.Media.Brush;
using WpfBrushes = System.Windows.Media.Brushes;
using WpfColor = System.Windows.Media.Color;
using WpfPen = System.Windows.Media.Pen;

namespace HsWin.App;

internal sealed partial class FollowingToastWindow : IToastView, INativeCursorFollowingToastView
{
    private static readonly MediaBrush FillBrush = CreateGrayscaleBrush(FollowingToastStyleMetrics.FillColorComponent);
    private static readonly MediaBrush OutlineBrush = CreateGrayscaleBrush(FollowingToastStyleMetrics.OutlineColorComponent);
    private static readonly MediaBrush TextBrush = CreateGrayscaleBrush(FollowingToastStyleMetrics.TextColorComponent);
    private static readonly WpfPen OutlinePen = CreateOutlinePen();
    private static readonly DropShadowEffect PillShadow = CreatePillShadow();

    private readonly BlurEffect _transitionBlur;
    private readonly UIElement _transitionTarget;
    private readonly ToastExitAnimator _exitAnimator;
    private readonly Popup _popup;
    private readonly Grid _root;
    private readonly TextBlock _text;
    private double _left;
    private double _top;

    public FollowingToastWindow()
    {
        _text = new TextBlock
        {
            FontFamily = ToastFonts.MediumFontFamily,
            FontSize = FollowingToastStyleMetrics.TextFontSize,
            FontWeight = FollowingToastStyleMetrics.TextFontWeight,
            Foreground = TextBrush,
            MaxWidth = FollowingToastStyleMetrics.TextMaxWidth,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center
        };

        var border = new PillBorder
        {
            Padding = new Thickness(
                FollowingToastStyleMetrics.HorizontalPadding,
                FollowingToastStyleMetrics.VerticalPadding,
                FollowingToastStyleMetrics.HorizontalPadding,
                FollowingToastStyleMetrics.VerticalPadding),
            Effect = PillShadow,
            Child = _text
        };

        _transitionBlur = new BlurEffect { Radius = FollowingToastStyleMetrics.TransitionBlurRadius };
        _transitionTarget = new Border { Effect = _transitionBlur, Child = border };
        _exitAnimator = new ToastExitAnimator(
            _transitionTarget,
            _transitionBlur,
            TimeSpan.FromMilliseconds(FollowingToastStyleMetrics.ExitDurationMs),
            FollowingToastStyleMetrics.TransitionBlurRadius,
            new CubicEase { EasingMode = EasingMode.EaseIn },
            new CubicEase { EasingMode = EasingMode.EaseInOut });

        _root = new Grid
        {
            Margin = new Thickness(FollowingToastStyleMetrics.ShadowInset),
            Background = WpfBrushes.Transparent,
            Focusable = false,
            IsHitTestVisible = false,
            UseLayoutRounding = true,
            SnapsToDevicePixels = true,
            Children = { _transitionTarget }
        };

        _popup = new Popup
        {
            AllowsTransparency = true,
            Child = _root,
            Focusable = false,
            IsHitTestVisible = false,
            Placement = PlacementMode.AbsolutePoint,
            StaysOpen = true
        };
    }

    public double ActualHeight => _root.ActualHeight;

    public double ActualWidth => _root.ActualWidth;

    public bool IsVisible => _popup.IsOpen;

    public double Left
    {
        get => _left;
        set
        {
            _left = value;
            _popup.HorizontalOffset = value;
        }
    }

    public double Top
    {
        get => _top;
        set
        {
            _top = value;
            _popup.VerticalOffset = value;
        }
    }

    public Visual PlacementVisual => _root;

    public nint NativeHandle =>
        (PresentationSource.FromVisual(_root) as HwndSource)?.Handle ?? nint.Zero;

    internal bool HasActiveTransitionAnimations =>
        _transitionTarget.HasAnimatedProperties && _transitionBlur.HasAnimatedProperties;

    public void Close()
    {
        _popup.IsOpen = false;
    }

    public void Hide()
    {
        _popup.IsOpen = false;
    }

    public void Show()
    {
        if (_popup.IsOpen)
        {
            return;
        }

        _popup.IsOpen = true;
        ConfigureNativeWindow();
    }

    public void UpdateLayout() => _root.UpdateLayout();

    public void UpdateRequest(AlertRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        // The cursor follower moves the popup HWND natively, off the UI thread. Adopt that position
        // before the text change relayouts the popup, or WPF snaps it back to the stale offsets.
        SyncOffsetsFromNativeWindow();
        _text.Text = request.Text;
    }

    private void SyncOffsetsFromNativeWindow()
    {
        var handle = NativeHandle;
        if (!_popup.IsOpen || handle == nint.Zero || !ScreenGeometry.TryGetWindowRect(handle, out var rect))
        {
            return;
        }

        var scale = ScreenGeometry.GetScaleAt(rect.Left, rect.Top);
        Left = rect.Left / scale;
        Top = rect.Top / scale;
    }

    public void PrepareForShow()
    {
        CancelExitAnimation();
        _transitionTarget.Opacity = 0;
        _transitionBlur.Radius = FollowingToastStyleMetrics.TransitionBlurRadius;
    }

    public void BeginEnterAnimation()
    {
        ClearEnterAnimations();
        var easing = new CubicEase { EasingMode = EasingMode.EaseOut };
        var duration = TimeSpan.FromMilliseconds(FollowingToastStyleMetrics.EnterDurationMs);

        _transitionTarget.BeginAnimation(
            UIElement.OpacityProperty,
            new DoubleAnimation(0, 1, duration) { EasingFunction = easing });
        _transitionBlur.BeginAnimation(
            BlurEffect.RadiusProperty,
            new DoubleAnimation(FollowingToastStyleMetrics.TransitionBlurRadius, 0, duration)
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut }
            });
    }

    public void BeginExitAnimation(Action onComplete)
    {
        ClearEnterAnimations();
        _transitionTarget.Opacity = 1;
        _transitionBlur.Radius = 0;
        _exitAnimator.Begin(onComplete);
    }

    public void CancelExitAnimation()
    {
        ClearEnterAnimations();
        _exitAnimator.Cancel();
    }

    private void ClearEnterAnimations()
    {
        _transitionTarget.BeginAnimation(UIElement.OpacityProperty, null);
        _transitionBlur.BeginAnimation(BlurEffect.RadiusProperty, null);
    }

    private void ConfigureNativeWindow()
    {
        var source = PresentationSource.FromVisual(_root) as HwndSource;
        if (source is null)
        {
            return;
        }

        var handle = source.Handle;
        var styles = NativeMethods.GetWindowLongPtr(handle, NativeMethods.ExtendedWindowStyleIndex);
        NativeMethods.SetWindowLongPtr(
            handle,
            NativeMethods.ExtendedWindowStyleIndex,
            styles
                | NativeMethods.TransparentStyle
                | NativeMethods.NoActivateStyle
                | NativeMethods.ToolWindowStyle);
        NativeMethods.SetWindowPos(
            handle,
            NativeMethods.TopmostWindow,
            0,
            0,
            0,
            0,
            NativeMethods.DoNotMove
                | NativeMethods.DoNotSize
                | NativeMethods.DoNotActivate);
    }

    private static MediaBrush CreateFrozenBrush(WpfColor color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    private static MediaBrush CreateGrayscaleBrush(byte component)
    {
        return CreateFrozenBrush(WpfColor.FromRgb(component, component, component));
    }

    private static DropShadowEffect CreatePillShadow()
    {
        var effect = new DropShadowEffect
        {
            Color = WpfColor.FromRgb(0, 0, 0),
            Opacity = FollowingToastStyleMetrics.ShadowOpacity,
            BlurRadius = FollowingToastStyleMetrics.ShadowBlurRadius,
            ShadowDepth = 0,
            RenderingBias = RenderingBias.Quality
        };
        effect.Freeze();
        return effect;
    }

    private static WpfPen CreateOutlinePen()
    {
        var pen = new WpfPen(OutlineBrush, FollowingToastStyleMetrics.OutlineWidth);
        pen.Freeze();
        return pen;
    }

    private sealed class PillBorder : Border
    {
        protected override void OnRender(DrawingContext drawingContext)
        {
            base.OnRender(drawingContext);

            var halfOutline = FollowingToastStyleMetrics.OutlineWidth / 2;
            var bounds = new Rect(
                halfOutline,
                halfOutline,
                Math.Max(0, ActualWidth - FollowingToastStyleMetrics.OutlineWidth),
                Math.Max(0, ActualHeight - FollowingToastStyleMetrics.OutlineWidth));
            var radius = Math.Max(0, bounds.Height / 2);
            drawingContext.DrawRoundedRectangle(FillBrush, OutlinePen, bounds, radius, radius);
        }
    }

    private static partial class NativeMethods
    {
        internal const int ExtendedWindowStyleIndex = -20;
        internal const nint TransparentStyle = 0x00000020;
        internal const nint ToolWindowStyle = 0x00000080;
        internal const nint NoActivateStyle = 0x08000000;
        internal static readonly nint TopmostWindow = new(-1);
        internal const uint DoNotSize = 0x0001;
        internal const uint DoNotMove = 0x0002;
        internal const uint DoNotActivate = 0x0010;

        [LibraryImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
        internal static partial nint GetWindowLongPtr(nint windowHandle, int index);

        [LibraryImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
        internal static partial nint SetWindowLongPtr(nint windowHandle, int index, nint newLong);

        [LibraryImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static partial bool SetWindowPos(
            nint windowHandle,
            nint insertAfter,
            int x,
            int y,
            int width,
            int height,
            uint flags);
    }
}
