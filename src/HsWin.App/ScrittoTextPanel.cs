using HsWin.Core.Alerts;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using WpfOrientation = System.Windows.Controls.Orientation;
using WpfPanel = System.Windows.Controls.Panel;
using WpfPoint = System.Windows.Point;
using WpfSize = System.Windows.Size;

namespace HsWin.App;

/// <summary>
/// Native WPF port of Scritto's <c>&lt;scritto-text&gt;</c> roll
/// (<c>packages/core/src/index.ts</c>, <c>helpers.ts</c>, <c>const.ts</c>).
/// A text change keeps the surviving glyphs sliding to their new places
/// (FLIP) while the rest roll out as ghosts and their replacements roll in,
/// with Scritto's stagger-by-position, spring, trend and pooling semantics.
/// Single-line only: toast values never wrap mid-word, so the multi-line
/// ghost groups and flow handoff from the web port have nothing to do here.
/// </summary>
///
/// <remarks>
/// Fidelity notes (each a deliberate, reviewed delta from the web engine):
/// <list type="bullet">
/// <item>Box width animates with the shrink easing, but the travelling-edge
/// gradient band is omitted: ghosts already dissolve on opacity, blur and
/// scale, and a mis-sized mask turns into the hard cut the mask exists to
/// avoid. Revisit if shrunken ghosts visibly overlap the status icon.</item>
/// <item>A width transition restarts from the width it has reached instead of
/// being left alone mid-flight, so rapid wheel updates converge instead of
/// landing on a stale target.</item>
/// <item>An offscreen but live panel still rolls: a toast reappearing with a
/// new number is seen, unlike a scrolled-out web element.</item>
/// </list>
/// </remarks>
internal sealed class ScrittoTextPanel : Grid
{
    /// <summary>Box anchor: the toast pill is centered, so both edges travel.</summary>
    private const double Anchor = 0.5;

    private const int PoolMax = 1024;

    private static readonly string NonBreakingSpaceText = new((char)0xA0, 1);

    private readonly StackPanel _live;
    private readonly Canvas _ghosts;
    private readonly Queue<GlyphBlock> _pool = new();
    private readonly List<GlyphBlock> _chars = new();
    private readonly List<TranslateTransform> _flips = new();
    private readonly IEasingFunction _shrinkEasing = new ScrittoShrinkEasing();

    private string _value = string.Empty;
    private bool _hasValue;
    private int _generation;

    /// <summary>
    /// Raised when a roll animates the content width, so the toast window can
    /// ride its left edge and stay centered while the pill grows or shrinks.
    /// </summary>
    public event Action<double, double, TimeSpan>? WidthRollStarted;

    public ScrittoTextPanel()
    {
        _live = new StackPanel
        {
            Orientation = WpfOrientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
            MaxWidth = ToastStyleMetrics.TextMaxWidth,
        };
        _ghosts = new Canvas
        {
            IsHitTestVisible = false,
        };
        Children.Add(_live);
        Children.Add(_ghosts);
    }

    public string CurrentValue => _value;

    public bool HasValue => _hasValue;

    /// <summary>Sets the text with no motion (Scritto's <c>value</c> setter).</summary>
    public void Snap(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        ThrowIfNotOnDispatcher();

        _generation++;
        ClearWidthAnimation();
        CancelFlips();
        foreach (var ghost in _ghosts.Children.OfType<GlyphBlock>().ToList())
        {
            Release(ghost);
        }

        _ghosts.Children.Clear();
        foreach (var live in _chars)
        {
            Release(live);
        }

        _chars.Clear();
        _live.Children.Clear();

        foreach (var label in ScrittoGraphemes.Split(value))
        {
            var block = Checkout(label);
            _chars.Add(block);
            _live.Children.Add(block);
        }

        _value = value;
        _hasValue = true;
    }

    /// <summary>
    /// Rolls to the next value (Scritto's <c>update()</c>). Falls back to
    /// <see cref="Snap"/> for equal values, first paint, zero duration, and
    /// reduced motion, mirroring the web engine's quiet no-ops.
    /// </summary>
    public void Roll(string value, int trendOption, int durationMs, bool bounce)
    {
        ArgumentNullException.ThrowIfNull(value);
        ThrowIfNotOnDispatcher();

        if (_hasValue && string.Equals(value, _value, StringComparison.Ordinal))
        {
            return;
        }

        if (!_hasValue || durationMs <= 0 || IsReducedMotion())
        {
            Snap(value);
            return;
        }

        var oldLabels = _chars.Select(static block => block.Text).ToList();
        var diff = ScrittoDiff.Compute(oldLabels, value, Anchor);
        if (diff.PrefixCount == oldLabels.Count && diff.PrefixCount == diff.Labels.Count)
        {
            _value = value;
            return;
        }

        var trend = trendOption != 0 ? Math.Sign(trendOption) : ScrittoTrend.Of(_value, value);
        var fontSize = TextElement.GetFontSize(this);
        if (fontSize <= 0)
        {
            fontSize = 16;
        }
        var duration = TimeSpan.FromMilliseconds(durationMs);
        var spring = new ScrittoSpringEasing(bounce);

        CancelFlips();
        _live.BeginAnimation(WidthProperty, null);

        var oldLayout = MeasureOffsets(_chars);
        var oldWidth = _live.ActualWidth;
        if (oldWidth <= 0)
        {
            Snap(value);
            return;
        }

        var exiting = _chars
            .Skip(diff.PrefixCount)
            .Take(Math.Max(0, diff.OldSuffix - diff.PrefixCount))
            .Concat(_chars.Skip(diff.OldSuffix + diff.SuffixCount))
            .ToList();
        var exitingOffsets = exiting.Select(block => oldLayout[block].Left).ToList();

        ParkExits(exiting);
        var enters = RebuildLive(diff);

        // Measure the natural width BEFORE pinning: an explicit Width caps
        // DesiredSize, so measuring under the pin below would read oldWidth.
        _live.Measure(new WpfSize(double.PositiveInfinity, double.PositiveInfinity));
        var newWidth = _live.DesiredSize.Width;

        _live.Width = oldWidth;
        _live.UpdateLayout();

        var newLayout = MeasureOffsets(_chars);

        var enterOffsets = enters.Select(block => newLayout[block].Left).ToList();
        var exitDelays = ScrittoTiming.StaggerDelays(exitingOffsets, durationMs);
        var enterDelays = ScrittoTiming.StaggerDelays(enterOffsets, durationMs);
        var exitTail = exitDelays.Length > 0 ? exitDelays.Max() : 0;
        var total = TimeSpan.FromMilliseconds(durationMs + exitTail);

        AnimateExits(exiting, exitDelays, trend, fontSize, duration, spring);
        FlipKept(diff, oldLayout, newLayout, total);
        AnimateEnters(enters, enterDelays, trend, fontSize, duration, spring);
        AnimateWidth(oldWidth, newWidth, total);

        _value = value;
        _hasValue = true;
    }

    /// <summary>
    /// Moves leaving glyphs onto the overlay layer at the exact place they
    /// were drawn, so the box can resize underneath without dragging them
    /// along. Interrupted entrance motion is discarded first (the web engine
    /// likewise replaces it), so parking measures pure layout. Ghost
    /// animations are never cancelled by the next update: each carries its
    /// own completion that releases it back to the pool.
    /// </summary>
    private void ParkExits(List<GlyphBlock> exiting)
    {
        foreach (var block in exiting)
        {
            block.ResetVisual();
            var position = block.TranslatePoint(new WpfPoint(0, 0), _ghosts);
            _live.Children.Remove(block);
            Canvas.SetLeft(block, position.X);
            Canvas.SetTop(block, position.Y);
            _ghosts.Children.Add(block);
        }
    }

    private List<GlyphBlock> RebuildLive(ScrittoDiffResult diff)
    {
        var keptPrefix = _chars.Take(diff.PrefixCount).ToList();
        var keptSuffix = _chars.Skip(diff.OldSuffix).Take(diff.SuffixCount).ToList();
        var enters = new List<GlyphBlock>();

        _live.Children.Clear();
        _chars.Clear();

        foreach (var block in keptPrefix)
        {
            _chars.Add(block);
            _live.Children.Add(block);
        }

        for (var index = diff.PrefixCount; index < diff.MidEnd; index++)
        {
            enters.Add(AddEnter(diff.Labels[index]));
        }

        foreach (var block in keptSuffix)
        {
            _chars.Add(block);
            _live.Children.Add(block);
        }

        for (var index = diff.MidEnd + diff.SuffixCount; index < diff.Labels.Count; index++)
        {
            enters.Add(AddEnter(diff.Labels[index]));
        }

        return enters;
    }

    private GlyphBlock AddEnter(string label)
    {
        var block = Checkout(label);
        if (!IsSpace(block))
        {
            block.Opacity = 0;
        }

        _chars.Add(block);
        _live.Children.Add(block);
        return block;
    }

    private void AnimateExits(
        List<GlyphBlock> exiting,
        double[] delays,
        int trend,
        double fontSize,
        TimeSpan duration,
        ScrittoSpringEasing spring)
    {
        var farY = -trend * ScrittoTiming.GlyphOffsetPx(fontSize);
        var farBlur = ScrittoTiming.GlyphBlurPx(fontSize);
        var generation = _generation;
        for (var i = 0; i < exiting.Count; i++)
        {
            var block = exiting[i];
            var delay = TimeSpan.FromMilliseconds(delays[i]);
            if (IsSpace(block))
            {
                RunEphemeral(block, delay, duration, generation);
                continue;
            }

            AnimateDouble(block, OpacityProperty, 1, 0, delay, duration, spring, null);
            AnimateDouble(block.Translate, TranslateTransform.YProperty, 0, farY, delay, duration, spring, null);
            AnimateDouble(block.Scale, ScaleTransform.ScaleXProperty, 1, ScrittoTiming.GlyphScale, delay, duration, spring, null);
            AnimateDouble(block.Scale, ScaleTransform.ScaleYProperty, 1, ScrittoTiming.GlyphScale, delay, duration, spring, null);
            AnimateDouble(block.Rotate, RotateTransform.AngleProperty, 0, ScrittoTiming.GlyphRotateDeg, delay, duration, spring, null);
            AnimateDouble(block.Blur, BlurEffect.RadiusProperty, 0, farBlur, delay, duration, spring, () =>
            {
                if (generation == _generation && ReferenceEquals(block.Parent, _ghosts))
                {
                    Release(block);
                }
            });
        }
    }

    private void FlipKept(
        ScrittoDiffResult diff,
        Dictionary<GlyphBlock, GlyphOffset> oldLayout,
        Dictionary<GlyphBlock, GlyphOffset> newLayout,
        TimeSpan total)
    {
        var kept = _chars
            .Take(diff.PrefixCount)
            .Concat(_chars.Skip(diff.MidEnd).Take(diff.SuffixCount));
        foreach (var block in kept)
        {
            if (!oldLayout.TryGetValue(block, out var oldOffset)
                || !newLayout.TryGetValue(block, out var newOffset))
            {
                continue;
            }

            var dx = oldOffset.CenterX - newOffset.CenterX;
            if (dx == 0)
            {
                continue;
            }

            var animation = new DoubleAnimation(dx, 0, total)
            {
                EasingFunction = _shrinkEasing,
                FillBehavior = FillBehavior.Stop,
            };
            block.Translate.BeginAnimation(TranslateTransform.XProperty, animation);
            _flips.Add(block.Translate);
        }
    }

    private void AnimateEnters(
        List<GlyphBlock> enters,
        double[] delays,
        int trend,
        double fontSize,
        TimeSpan duration,
        ScrittoSpringEasing spring)
    {
        var nearY = trend * ScrittoTiming.GlyphOffsetPx(fontSize);
        var nearBlur = ScrittoTiming.GlyphBlurPx(fontSize);
        for (var i = 0; i < enters.Count; i++)
        {
            var block = enters[i];
            var delay = TimeSpan.FromMilliseconds(delays[i]);
            if (IsSpace(block))
            {
                continue;
            }

            block.Translate.Y = nearY;
            block.Scale.ScaleX = ScrittoTiming.GlyphScale;
            block.Scale.ScaleY = ScrittoTiming.GlyphScale;
            block.Rotate.Angle = ScrittoTiming.GlyphRotateDeg;
            block.Blur.Radius = nearBlur;

            AnimateDouble(block, OpacityProperty, 0, 1, delay, duration, spring, null);
            AnimateDouble(block.Translate, TranslateTransform.YProperty, nearY, 0, delay, duration, spring, null);
            AnimateDouble(block.Scale, ScaleTransform.ScaleXProperty, ScrittoTiming.GlyphScale, 1, delay, duration, spring, null);
            AnimateDouble(block.Scale, ScaleTransform.ScaleYProperty, ScrittoTiming.GlyphScale, 1, delay, duration, spring, null);
            AnimateDouble(block.Rotate, RotateTransform.AngleProperty, ScrittoTiming.GlyphRotateDeg, 0, delay, duration, spring, null);
            AnimateDouble(block.Blur, BlurEffect.RadiusProperty, nearBlur, 0, delay, duration, spring, null);
        }
    }

    private void AnimateWidth(double oldWidth, double newWidth, TimeSpan total)
    {
        if (Math.Abs(newWidth - oldWidth) < ScrittoTiming.MinWidthDeltaPx)
        {
            _live.BeginAnimation(WidthProperty, null);
            _live.Width = double.NaN;
            return;
        }

        var animation = new DoubleAnimation(oldWidth, newWidth, total)
        {
            EasingFunction = _shrinkEasing,
            FillBehavior = FillBehavior.Stop,
        };
        _live.BeginAnimation(WidthProperty, animation);
        _live.Width = double.NaN;
        WidthRollStarted?.Invoke(oldWidth, newWidth, total);
    }

    /// <summary>
    /// A space has no ink to roll, so it runs an animation that does nothing
    /// rather than a wall-clock timer, holding its slot on the same clock as
    /// the glyphs beside it.
    /// </summary>
    private void RunEphemeral(GlyphBlock block, TimeSpan delay, TimeSpan duration, int generation)
    {
        var animation = new DoubleAnimation(1, 1, duration)
        {
            BeginTime = delay,
            FillBehavior = FillBehavior.Stop,
        };
        animation.Completed += (_, _) =>
        {
            if (generation == _generation && ReferenceEquals(block.Parent, _ghosts))
            {
                Release(block);
            }
        };
        block.BeginAnimation(OpacityProperty, animation);
    }

    private static void AnimateDouble(
        DependencyObject target,
        DependencyProperty property,
        double from,
        double to,
        TimeSpan delay,
        TimeSpan duration,
        IEasingFunction easing,
        Action? onComplete)
    {
        var animation = new DoubleAnimation(from, to, duration)
        {
            BeginTime = delay,
            EasingFunction = easing,
        };
        if (onComplete is not null)
        {
            animation.Completed += (_, _) => onComplete();
        }

        ((IAnimatable)target).BeginAnimation(property, animation, HandoffBehavior.SnapshotAndReplace);
    }

    private void CancelFlips()
    {
        foreach (var translate in _flips)
        {
            translate.BeginAnimation(TranslateTransform.XProperty, null);
        }

        _flips.Clear();
    }

    private void ClearWidthAnimation()
    {
        _live.BeginAnimation(WidthProperty, null);
        _live.Width = double.NaN;
    }

    private static Dictionary<GlyphBlock, GlyphOffset> MeasureOffsets(IReadOnlyList<GlyphBlock> blocks)
    {
        var layout = new Dictionary<GlyphBlock, GlyphOffset>(blocks.Count);
        var x = 0.0;
        foreach (var block in blocks)
        {
            layout[block] = new GlyphOffset(x, block.ActualWidth);
            x += block.ActualWidth;
        }

        return layout;
    }

    private static bool IsSpace(GlyphBlock block)
    {
        return string.Equals(block.Text, NonBreakingSpaceText, StringComparison.Ordinal);
    }

    private static bool IsReducedMotion()
    {
        try
        {
            return !SystemParameters.ClientAreaAnimation;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private GlyphBlock Checkout(string text)
    {
        if (_pool.TryDequeue(out var block))
        {
            block.ResetVisual();
            block.Text = text;
            return block;
        }

        return new GlyphBlock { Text = text };
    }

    private void Release(GlyphBlock block)
    {
        block.ResetVisual();
        block.RemoveFromParent();
        if (_pool.Count < PoolMax)
        {
            _pool.Enqueue(block);
        }
    }

    private void ThrowIfNotOnDispatcher()
    {
        if (!Dispatcher.CheckAccess())
        {
            throw new InvalidOperationException("ScrittoTextPanel must be used on its dispatcher thread.");
        }
    }

    private sealed record GlyphOffset(double Left, double Width)
    {
        public double CenterX => Left + Width / 2;
    }

    private sealed class GlyphBlock : TextBlock
    {
        public readonly TranslateTransform Translate = new();
        public readonly ScaleTransform Scale = new(1, 1);
        public readonly RotateTransform Rotate = new();
        public readonly BlurEffect Blur = new() { Radius = 0 };

        public GlyphBlock()
        {
            var group = new TransformGroup();
            group.Children.Add(Translate);
            group.Children.Add(Scale);
            group.Children.Add(Rotate);
            RenderTransform = group;
            RenderTransformOrigin = new WpfPoint(0.5, 0.5);
            VerticalAlignment = VerticalAlignment.Center;
            Effect = Blur;
        }

        public void ResetVisual()
        {
            BeginAnimation(OpacityProperty, null);
            Translate.BeginAnimation(TranslateTransform.XProperty, null);
            Translate.BeginAnimation(TranslateTransform.YProperty, null);
            Scale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
            Scale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
            Rotate.BeginAnimation(RotateTransform.AngleProperty, null);
            Blur.BeginAnimation(BlurEffect.RadiusProperty, null);
            Opacity = 1;
            Translate.X = 0;
            Translate.Y = 0;
            Scale.ScaleX = 1;
            Scale.ScaleY = 1;
            Rotate.Angle = 0;
            Blur.Radius = 0;
        }

        public void RemoveFromParent()
        {
            if (Parent is WpfPanel panel)
            {
                panel.Children.Remove(this);
            }
        }
    }
}
