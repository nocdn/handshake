using System.Threading;
using System.Windows;
using System.Windows.Threading;

namespace HsWin.App.Tests;

public sealed class ScrittoTextPanelTests
{
    [Fact]
    public void SnapSetsCurrentValue()
    {
        var (value, hasValue) = RunOnStaThread(() =>
        {
            var panel = new ScrittoTextPanel();
            panel.Snap("Count 50");
            return (panel.CurrentValue, panel.HasValue);
        });

        Assert.Equal("Count 50", value);
        Assert.True(hasValue);
    }

    [Fact]
    public void EqualRollIsNoOp()
    {
        var eventCount = 0;
        var value = RunOnStaThread(() =>
        {
            var panel = LayoutPanel("Count 50");
            panel.WidthRollStarted += (_, _, _) => eventCount++;
            panel.Roll("Count 50", 1, 300, bounce: false);
            return panel.CurrentValue;
        });

        Assert.Equal("Count 50", value);
        Assert.Equal(0, eventCount);
    }

    [Fact]
    public void RollWithoutLayoutFallsBackToSnap()
    {
        var eventCount = 0;
        var value = RunOnStaThread(() =>
        {
            var panel = new ScrittoTextPanel();
            panel.WidthRollStarted += (_, _, _) => eventCount++;
            panel.Roll("Count 51", 1, 300, bounce: false);
            return panel.CurrentValue;
        });

        Assert.Equal("Count 51", value);
        Assert.Equal(0, eventCount);
    }

    [Fact]
    public void InterruptedRollKeepsLatestValue()
    {
        var value = RunOnStaThread(() =>
        {
            var panel = LayoutPanel("Count 50");
            panel.Roll("Count 51", 1, 300, bounce: false);
            panel.Roll("Count 52", 1, 300, bounce: false);
            panel.Roll("Count 100", -1, 300, bounce: true);
            return panel.CurrentValue;
        });

        Assert.Equal("Count 100", value);
    }

    [Fact]
    public void ZeroDurationSnaps()
    {
        var eventCount = 0;
        var value = RunOnStaThread(() =>
        {
            var panel = LayoutPanel("Count 50");
            panel.WidthRollStarted += (_, _, _) => eventCount++;
            panel.Roll("Count 51", 1, 0, bounce: false);
            return panel.CurrentValue;
        });

        Assert.Equal("Count 51", value);
        Assert.Equal(0, eventCount);
    }

    [Fact]
    public void WidthChangeRaisesWidthRollOnlyWhenAnimated()
    {
        var fired = false;
        var (oldWidth, newWidth, duration) = (0.0, 0.0, TimeSpan.Zero);
        var value = RunOnStaThread(() =>
        {
            var panel = LayoutPanel("9");
            panel.WidthRollStarted += (before, after, total) =>
            {
                fired = true;
                oldWidth = before;
                newWidth = after;
                duration = total;
            };
            panel.Roll("10", 1, 300, bounce: false);
            return panel.CurrentValue;
        });

        Assert.Equal("10", value);
        Assert.Equal(SystemParameters.ClientAreaAnimation, fired);
        if (fired)
        {
            Assert.True(oldWidth > 0);
            Assert.True(newWidth > oldWidth);
            Assert.True(duration >= TimeSpan.FromMilliseconds(300));
        }
    }

    private static ScrittoTextPanel LayoutPanel(string value)
    {
        var panel = new ScrittoTextPanel();
        panel.Snap(value);
        panel.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        panel.Arrange(new Rect(0, 0, panel.DesiredSize.Width, panel.DesiredSize.Height));
        panel.UpdateLayout();
        return panel;
    }

    private static T RunOnStaThread<T>(Func<T> action)
    {
        T? result = default;
        Exception? thrown = null;
        var thread = new Thread(() =>
        {
            try
            {
                result = action();
            }
            catch (Exception exception)
            {
                thrown = exception;
            }
            finally
            {
                Dispatcher.CurrentDispatcher.InvokeShutdown();
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (thrown is not null)
        {
            throw thrown;
        }

        return result!;
    }
}
