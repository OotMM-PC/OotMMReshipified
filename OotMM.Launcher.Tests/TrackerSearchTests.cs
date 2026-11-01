using OotMM.Launcher.ViewModels;

namespace OotMM.Launcher.Tests;

public sealed class TrackerSearchTests
{
    private static TrackerTabViewModel Tracker()
    {
        var tracker = new TrackerTabViewModel();
        tracker.LoadSampleData();
        return tracker;
    }

    [Fact]
    public void EveryWordMustMatchIgnoringPunctuation()
    {
        var tracker = Tracker();
        tracker.HideCollected = false;
        tracker.OnlyInLogic = false;

        tracker.FilterText = "anjus cucco";
        Assert.Equal(["Kakariko — Anju's Cuccos"], tracker.VisibleChecks.Select(c => c.Name));

        tracker.FilterText = "mm heart";
        Assert.Equal(["Great Bay — Zora Cape Heart Piece"], tracker.VisibleChecks.Select(c => c.Name));
    }

    [Fact]
    public void CollectedItemsAreSearchableButUncollectedItemsAreNot()
    {
        var tracker = Tracker();
        tracker.HideCollected = false;

        tracker.FilterText = "slingshot";
        Assert.Equal(["Astral Observatory — Moon's Tear"], tracker.VisibleChecks.Select(c => c.Name));
    }

    [Fact]
    public void ReportsMatchesHiddenByFiltersAndCanRevealThem()
    {
        var tracker = Tracker();

        tracker.FilterText = "boss";
        Assert.Empty(tracker.VisibleChecks);
        Assert.Equal(2, tracker.HiddenMatches);

        tracker.ShowAllMatches();
        Assert.Equal(2, tracker.VisibleChecks.Count);
        Assert.Equal(0, tracker.HiddenMatches);
        Assert.False(tracker.OnlyInLogic);
    }
}
