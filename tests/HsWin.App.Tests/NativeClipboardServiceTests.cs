using HsWin.App.Clipboard;
using HsWin.Core.Clipboard;
using HsWin.Core.Logging;

namespace HsWin.App.Tests;

public sealed class NativeClipboardServiceTests
{
    [Fact]
    public void WatchReadsTextSnapshotForEachClipboardNotification()
    {
        var textStore = new CapturingClipboardTextStore("npm install react", hasText: true);
        var notifier = new CapturingClipboardChangeNotifier();
        using var service = new NativeClipboardService(textStore, notifier);
        var snapshots = new List<ClipboardChangeSnapshot>();

        using var watch = service.Watch(snapshots.Add);
        notifier.Trigger();
        textStore.Contents = new ClipboardTextContents(string.Empty, HasText: false);
        notifier.Trigger();

        Assert.Collection(
            snapshots,
            snapshot =>
            {
                Assert.Equal(1, snapshot.Sequence);
                Assert.Equal("npm install react", snapshot.Contents);
                Assert.True(snapshot.HasText);
            },
            snapshot =>
            {
                Assert.Equal(2, snapshot.Sequence);
                Assert.Equal(string.Empty, snapshot.Contents);
                Assert.False(snapshot.HasText);
            });
    }

    [Fact]
    public void DisposedWatchStopsReceivingClipboardNotifications()
    {
        var textStore = new CapturingClipboardTextStore("npm install react", hasText: true);
        var notifier = new CapturingClipboardChangeNotifier();
        using var service = new NativeClipboardService(textStore, notifier);
        var count = 0;

        var watch = service.Watch(_ => count++);
        watch.Dispose();
        notifier.Trigger();

        Assert.Equal(0, count);
    }

    private sealed class CapturingClipboardTextStore : IClipboardTextStore
    {
        public CapturingClipboardTextStore(string text, bool hasText)
        {
            Contents = new ClipboardTextContents(text, hasText);
        }

        public ClipboardTextContents Contents { get; set; }

        public ClipboardTextContents Read()
        {
            return Contents;
        }

        public bool Write(string text)
        {
            Contents = new ClipboardTextContents(text, text.Length > 0);
            return true;
        }

        public void Dispose()
        {
        }
    }

    private sealed class CapturingClipboardChangeNotifier : IClipboardChangeNotifier
    {
        private readonly List<CapturingRegistration> _registrations = [];

        public IDisposable Watch(Action changed)
        {
            var registration = new CapturingRegistration(changed);
            _registrations.Add(registration);
            return registration;
        }

        public void Trigger()
        {
            foreach (var registration in _registrations.Where(registration => !registration.IsDisposed).ToArray())
            {
                registration.Trigger();
            }
        }

        public void Dispose()
        {
            foreach (var registration in _registrations)
            {
                registration.Dispose();
            }
        }
    }

    private sealed class CapturingRegistration : IDisposable
    {
        private readonly Action _changed;

        public CapturingRegistration(Action changed)
        {
            _changed = changed;
        }

        public bool IsDisposed { get; private set; }

        public void Trigger()
        {
            _changed();
        }

        public void Dispose()
        {
            IsDisposed = true;
        }
    }
}
