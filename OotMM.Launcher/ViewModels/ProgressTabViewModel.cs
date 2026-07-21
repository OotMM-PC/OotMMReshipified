using System.Collections.ObjectModel;

namespace OotMM.Launcher.ViewModels;

public sealed class ProgressItemViewModel(string label, int have, int total, string detail = "")
    : ViewModelBase
{
    public string Label { get; } = label;
    public int Have { get; } = have;
    public int Total { get; } = total;
    public string Detail { get; } = detail;
    public bool HasDetail => Detail.Length > 0;
    public string CountText => Total > 0 ? $"{Have}/{Total}" : "";
    public bool IsComplete => Total > 0 && Have >= Total;
}

public sealed class ProgressSectionViewModel(string title)
{
    public string Title { get; } = title;
    public ObservableCollection<ProgressItemViewModel> Items { get; } = [];
}

/// <summary>
/// Both-games-at-a-glance progress tab (compass icon): dungeons, souls, songs,
/// key items. Sections are rebuilt by the Progress module port from the tracker
/// state; the shape here is the view contract it fills.
/// </summary>
public sealed class ProgressTabViewModel : ViewModelBase
{
    public ObservableCollection<ProgressSectionViewModel> Sections { get; } = [];

    public void SetSections(IEnumerable<ProgressSectionViewModel> sections)
    {
        Sections.Clear();
        foreach (var section in sections)
        {
            Sections.Add(section);
        }
    }

    public void LoadSampleData()
    {
        ProgressSectionViewModel Section(string title, params (string Label, int Have, int Total)[] items)
        {
            var section = new ProgressSectionViewModel(title);
            foreach (var (label, have, total) in items)
            {
                section.Items.Add(new ProgressItemViewModel(label, have, total));
            }

            return section;
        }

        SetSections(
        [
            Section("Dungeons",
                ("Deku Tree", 4, 4), ("Dodongo's Cavern", 2, 5), ("Woodfall Temple", 0, 6)),
            Section("Songs",
                ("OoT Songs", 3, 12), ("MM Songs", 1, 10)),
            Section("Key Items",
                ("Ocarina", 1, 1), ("Hookshot", 0, 2), ("Bomb Bag", 1, 3)),
            Section("Souls",
                ("Boss Souls", 1, 8), ("Enemy Souls", 5, 40)),
        ]);
    }
}
