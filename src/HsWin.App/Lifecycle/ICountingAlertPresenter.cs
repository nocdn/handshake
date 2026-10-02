using HsWin.Core.Alerts;

namespace HsWin.App.Lifecycle;

/// <summary>
/// An alert presenter that counts the content toasts taking its standard channel, so a lifecycle
/// receipt ("Config reloaded") can yield to a newer toast that appeared while it was pending.
/// </summary>
internal interface ICountingAlertPresenter : IAlertPresenter
{
    long StandardContentShows { get; }
}
