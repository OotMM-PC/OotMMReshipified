using OotMM.Launcher.ViewModels;

namespace OotMM.Launcher.Tests;

public sealed class TrackerScrollTests
{
    private static TrackerTabViewModel Tracker()
    {
        var tracker = new TrackerTabViewModel();
        tracker.SetChecks(Enumerable.Range(0, 6).Select(i =>
            new CheckRowViewModel(i % 2 == 0 ? "OOT" : "MM", $"Check {i}", "item", $"check{i}") { InLogic = true }));
        return tracker;
    }

    [Fact]
    public void RowsKeepTheirIdentityWhenAnEarlierCheckIsCollected()
    {
        var tracker = Tracker();
        var token = tracker.ScrollResetToken;
        var top = tracker.KeyAt(2);

        tracker.VisibleChecks[0].Collected = true;
        tracker.NotifyCheckStatesChanged();

        Assert.Equal(token, tracker.ScrollResetToken);
        Assert.Equal(1, tracker.IndexOfKey(top));
        Assert.Equal(-1, tracker.IndexOfKey("OOT:check0"));
    }

    [Fact]
    public void OnlyFilterChangesAskTheListToReturnToTheTop()
    {
        var tracker = Tracker();
        var token = tracker.ScrollResetToken;

        tracker.FilterText = "mm";
        Assert.NotEqual(token, tracker.ScrollResetToken);

        token = tracker.ScrollResetToken;
        tracker.GameFilterIndex = 1;
        tracker.HideCollected = false;
        tracker.OnlyInLogic = false;
        Assert.Equal(token + 3, tracker.ScrollResetToken);
    }
}
