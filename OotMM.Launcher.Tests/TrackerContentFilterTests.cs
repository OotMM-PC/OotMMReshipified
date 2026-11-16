using OotMM.Core;
using OotMM.Launcher.ViewModels;

namespace OotMM.Launcher.Tests;

public sealed class TrackerContentFilterTests
{
    private static CheckRowViewModel Row(string tag, string id, ContentClass content, bool required = false,
                                         bool collected = false, bool inLogic = true) =>
        new(tag, id, "item", id) { InLogic = inLogic, Content = content, Required = required, Collected = collected };

    private static TrackerTabViewModel Tracker(bool withRequiredData = false)
    {
        var tracker = new TrackerTabViewModel { HideCollected = false, ContentFiltersEnabled = true };
        tracker.SetChecks(
        [
            Row("OOT", "oot-major", ContentClass.Major, required: true),
            Row("OOT", "oot-key", ContentClass.Key),
            Row("OOT", "oot-junk", ContentClass.Normal),
            Row("MM", "mm-major", ContentClass.Major),
            Row("MM", "mm-token", ContentClass.Spider),
            Row("MM", "mm-major-done", ContentClass.Major, collected: true),
            Row("MM", "mm-major-far", ContentClass.Major, inLogic: false),
        ], withRequiredData);
        return tracker;
    }

    private static int IndexOf(TrackerTabViewModel tracker, string label) =>
        Array.FindIndex(tracker.OotContentLabels.Split('|'), entry => entry == label || entry.StartsWith(label + " ("));

    private static IEnumerable<string> Visible(TrackerTabViewModel tracker) =>
        tracker.VisibleChecks.Select(c => c.CheckId);

    [Fact]
    public void EachGameStaysLockedUntilItRevealsItsContents()
    {
        var tracker = Tracker();

        tracker.OotContentFilterIndex = IndexOf(tracker, "Major items");
        tracker.MmContentFilterIndex = IndexOf(tracker, "Major items");

        Assert.False(tracker.OotContentUnlocked);
        Assert.False(tracker.MmContentUnlocked);
        Assert.Equal(0, tracker.OotContentFilterIndex);
        Assert.Equal(0, tracker.MmContentFilterIndex);
        Assert.Equal(6, tracker.VisibleChecks.Count);
    }

    [Fact]
    public void FiltersStayHiddenUntilThePlayerTurnsThemOn()
    {
        var tracker = new TrackerTabViewModel();
        tracker.SetContentAccess(oot: true, mm: true, separateStones: true);

        Assert.False(tracker.ShowOotContentFilter);
        Assert.False(tracker.ShowMmContentFilter);
        tracker.OotContentFilterIndex = 2;
        Assert.Equal(0, tracker.OotContentFilterIndex);

        tracker.ContentFiltersEnabled = true;
        Assert.True(tracker.ShowOotContentFilter);
        Assert.True(tracker.ShowMmContentFilter);
    }

    [Fact]
    public void TurningFiltersOffClearsThem()
    {
        var tracker = Tracker();
        tracker.SetContentAccess(oot: true, mm: true, separateStones: true);
        tracker.MmContentFilterIndex = IndexOf(tracker, "Skulltula tokens");

        tracker.ContentFiltersEnabled = false;

        Assert.Equal(0, tracker.MmContentFilterIndex);
        Assert.Equal(6, tracker.VisibleChecks.Count);
        Assert.DoesNotContain("(", tracker.MmContentLabels);
    }

    [Fact]
    public void ChoicesCountWhatTheListWouldShowOnlyForUnlockedGames()
    {
        var tracker = Tracker();
        tracker.SetContentAccess(oot: false, mm: true, separateStones: true);

        Assert.Equal("Any contents|Major items|Small keys|Boss keys|Skulltula tokens|Stray fairies|Hearts|Souls|Maps and compasses",
                     tracker.OotContentLabels);
        Assert.Contains("Major items (2)", tracker.MmContentLabels.Split('|'));
        Assert.Contains("Skulltula tokens (1)", tracker.MmContentLabels.Split('|'));
        Assert.Contains("Small keys (0)", tracker.MmContentLabels.Split('|'));

        tracker.VisibleChecks.First(c => c.CheckId == "mm-token").Collected = true;
        tracker.NotifyCheckStatesChanged();

        Assert.Contains("Skulltula tokens (1)", tracker.MmContentLabels.Split('|'));
    }

    [Fact]
    public void CountsFollowTheOtherFilters()
    {
        var tracker = Tracker();
        tracker.SetContentAccess(oot: true, mm: true, separateStones: true);
        Assert.Contains("Major items (2)", tracker.MmContentLabels.Split('|'));

        tracker.OnlyInLogic = false;
        Assert.Contains("Major items (3)", tracker.MmContentLabels.Split('|'));

        tracker.HideCollected = true;
        Assert.Contains("Major items (2)", tracker.MmContentLabels.Split('|'));

        tracker.OnlyInLogic = true;
        Assert.Contains("Major items (1)", tracker.MmContentLabels.Split('|'));

        tracker.FilterText = "token";
        Assert.Contains("Major items (0)", tracker.MmContentLabels.Split('|'));
        Assert.Contains("Skulltula tokens (1)", tracker.MmContentLabels.Split('|'));
    }

    [Fact]
    public void EachGameFollowsItsOwnFilter()
    {
        var tracker = Tracker();
        tracker.SetContentAccess(oot: true, mm: true, separateStones: true);

        tracker.MmContentFilterIndex = IndexOf(tracker, "Skulltula tokens");
        Assert.Equal(["oot-major", "oot-key", "oot-junk", "mm-token"], Visible(tracker));

        tracker.OotContentFilterIndex = IndexOf(tracker, "Small keys");
        Assert.Equal(["oot-key", "mm-token"], Visible(tracker));
    }

    [Fact]
    public void OneStoneUnlocksOnlyItsOwnGame()
    {
        var tracker = Tracker();
        tracker.SetContentAccess(oot: false, mm: true, separateStones: true);

        tracker.OotContentFilterIndex = IndexOf(tracker, "Major items");
        tracker.MmContentFilterIndex = IndexOf(tracker, "Major items");

        Assert.Equal(0, tracker.OotContentFilterIndex);
        Assert.Equal(["oot-major", "oot-key", "oot-junk", "mm-major", "mm-major-done"], Visible(tracker));
        Assert.Equal("Locked: find the OoT Stone of Agony", tracker.OotContentLockedText);
    }

    [Fact]
    public void LockedTextFollowsTheSeedsStoneRules()
    {
        var tracker = Tracker();

        tracker.SetContentAccess(oot: false, mm: false, separateStones: false);
        Assert.Equal("Locked: find the Stone of Agony", tracker.MmContentLockedText);
    }

    [Fact]
    public void AFilterOnlyShowsForTheGamesBeingListed()
    {
        var tracker = Tracker();
        Assert.True(tracker.ShowOotContentFilter);
        Assert.True(tracker.ShowMmContentFilter);

        tracker.GameFilterIndex = 2;

        Assert.False(tracker.ShowOotContentFilter);
        Assert.True(tracker.ShowMmContentFilter);
    }

    [Fact]
    public void MatchesHiddenByOtherFiltersAreCounted()
    {
        var tracker = Tracker();
        tracker.HideCollected = true;
        tracker.SetContentAccess(oot: true, mm: true, separateStones: true);
        tracker.GameFilterIndex = 2;

        tracker.MmContentFilterIndex = IndexOf(tracker, "Major items");

        Assert.Equal(["mm-major"], Visible(tracker));
        Assert.Equal(2, tracker.HiddenMatches);
    }

    [Fact]
    public void WayOfTheHeroIsOfferedOnlyWhenTheSeedCarriesIt()
    {
        Assert.Equal(-1, IndexOf(Tracker(), "Way of the Hero"));

        var tracker = Tracker(withRequiredData: true);
        tracker.SetContentAccess(oot: true, mm: true, separateStones: true);
        tracker.GameFilterIndex = 1;
        tracker.OotContentFilterIndex = IndexOf(tracker, "Way of the Hero");

        Assert.Equal(["oot-major"], Visible(tracker));
    }

    [Fact]
    public void LosingAccessClearsThatGamesFilter()
    {
        var tracker = Tracker();
        tracker.SetContentAccess(oot: true, mm: true, separateStones: true);
        tracker.OotContentFilterIndex = IndexOf(tracker, "Small keys");
        tracker.MmContentFilterIndex = IndexOf(tracker, "Skulltula tokens");

        tracker.SetContentAccess(oot: false, mm: true, separateStones: true);

        Assert.Equal(0, tracker.OotContentFilterIndex);
        Assert.Equal(["oot-major", "oot-key", "oot-junk", "mm-token"], Visible(tracker));
    }
}
