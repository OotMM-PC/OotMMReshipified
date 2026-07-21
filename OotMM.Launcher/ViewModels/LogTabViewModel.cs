using System.Collections.ObjectModel;

namespace OotMM.Launcher.ViewModels;

public sealed record LogEntryViewModel(string Time, string Message, bool IsRemote);

/// <summary>
/// The check-event feed (bombers notebook tab): what was found, what arrived
/// from other players. The coordinator appends entries as packets flow.
/// </summary>
public sealed class LogTabViewModel : ViewModelBase
{
    public ObservableCollection<LogEntryViewModel> Entries { get; } = [];

    public bool IsEmpty => Entries.Count == 0;

    public void Add(string time, string message, bool isRemote = false)
    {
        Entries.Add(new LogEntryViewModel(time, message, isRemote));
        Raise(nameof(IsEmpty));
    }

    public void LoadSampleData()
    {
        Add("19:02", "Session started — Ocarina of Time");
        Add("19:04", "Deku Tree — Map Chest: found Kokiri Sword");
        Add("19:11", "Received Bombers' Notebook from Player 2 (Saria)", isRemote: true);
        Add("19:12", "Market — Mask Shop Delivery: sent MM Song of Soaring to Player 3", isRemote: true);
        Add("19:15", "Astral Observatory — Moon's Tear: found OOT Slingshot");
    }
}
