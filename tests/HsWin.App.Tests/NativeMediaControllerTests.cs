using HsWin.App.Media;

namespace HsWin.App.Tests;

public sealed class NativeMediaControllerTests
{
    [Theory]
    [InlineData("paused", "playing", "played")]
    [InlineData("stopped", "playing", "played")]
    [InlineData("playing", "paused", "paused")]
    [InlineData("playing", "stopped", "paused")]
    [InlineData("playing", "unknown", "paused")]
    [InlineData("paused", "unknown", "played")]
    [InlineData("unknown", "unknown", "toggled")]
    [InlineData("unknown", "playing", "played")]
    [InlineData("playing", "playing", "paused")]
    [InlineData("paused", "paused", "played")]
    public void InferPlayPauseActionUsesBeforeAndAfterPlaybackState(
        string statusBefore,
        string statusAfter,
        string expectedAction)
    {
        Assert.Equal(expectedAction, NativeMediaController.InferPlayPauseAction(statusBefore, statusAfter));
    }
}
