using OotMM.Launcher.Models;

namespace OotMM.Launcher.ViewModels;

/// <summary>A single row in a game card's mods list, with priority controls.</summary>
public sealed class ModEntryViewModel : ViewModelBase
{
    private readonly Action _onToggled;
    private bool _enabled;
    private bool _isFirst;
    private bool _isLast;

    public ModEntryViewModel(ModInfo info, Action onToggled, Action moveUp, Action moveDown)
    {
        Info = info;
        _onToggled = onToggled;
        _enabled = info.EnabledOnDisk;
        MoveUpCommand = new RelayCommand(moveUp);
        MoveDownCommand = new RelayCommand(moveDown);
    }

    public ModInfo Info { get; }

    public string Name => Info.Name;

    /// <summary>Required archives (cross-game models/music) render locked-on:
    /// the checkbox is disabled because disabling them breaks the seed.</summary>
    public bool Required => Info.Required;

    public RelayCommand MoveUpCommand { get; }
    public RelayCommand MoveDownCommand { get; }

    public bool IsFirst
    {
        get => _isFirst;
        set => Set(ref _isFirst, value);
    }

    public bool IsLast
    {
        get => _isLast;
        set => Set(ref _isLast, value);
    }

    public bool Enabled
    {
        get => _enabled;
        set
        {
            if (!Required && Set(ref _enabled, value))
            {
                _onToggled();
            }
        }
    }
}
