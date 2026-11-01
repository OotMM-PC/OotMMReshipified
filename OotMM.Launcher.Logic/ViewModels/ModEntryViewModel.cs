using OotMM.Launcher.Models;

namespace OotMM.Launcher.ViewModels;

public sealed class ModEntryViewModel : ViewModelBase
{
    private readonly Action _onToggled;
    private bool _enabled;
    private bool _isFirst;
    private bool _isLast;
    private bool _isSelected;

    public ModEntryViewModel(ModInfo info, Action onToggled)
    {
        Info = info;
        _onToggled = onToggled;
        _enabled = info.EnabledOnDisk;
    }

    internal static ModEntryViewModel Empty { get; } = new(new ModInfo("", "", false, false), () => { });

    public ModInfo Info { get; private set; }

    public string Name => Info.Name;

    public bool Required => Info.Required;

    internal void Refresh(ModInfo info)
    {
        Info = info;
        Set(ref _enabled, info.EnabledOnDisk, nameof(Enabled));
    }

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
