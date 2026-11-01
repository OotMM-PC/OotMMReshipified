using OotMM.Launcher.Models;

namespace OotMM.Launcher.ViewModels;

public sealed class ModEntryViewModel : ViewModelBase
{
    private readonly Action _onToggled;
    private readonly Action _moveUp;
    private readonly Action _moveDown;
    private bool _enabled;
    private bool _isFirst;
    private bool _isLast;
    private bool _isSelected;

    public ModEntryViewModel(ModInfo info, Action onToggled, Action moveUp, Action moveDown)
    {
        Info = info;
        _onToggled = onToggled;
        _enabled = info.EnabledOnDisk;
        _moveUp = moveUp;
        _moveDown = moveDown;
    }

    public ModInfo Info { get; }

    public string Name => Info.Name;

    public bool Required => Info.Required;

    public void MoveUp() => _moveUp();

    public void MoveDown() => _moveDown();

    public bool IsFirst
    {
        get => _isFirst;
        set => Set(ref _isFirst, value);
    }

    public bool IsSelected
    {
        get => _isSelected;
        set => Set(ref _isSelected, value);
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
