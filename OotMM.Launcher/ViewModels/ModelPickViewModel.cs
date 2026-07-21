namespace OotMM.Launcher.ViewModels;

/// <summary>
/// One checkbox in the co-op player-model pickers. A "model" is a set of mod
/// archives (child + adult + textures + voice); the checked set is announced to
/// the other players in a multiworld session — none checked means vanilla Link.
/// </summary>
public sealed class ModelPickViewModel : ViewModelBase
{
    private readonly Action _onChanged;
    private bool _isSelected;

    public ModelPickViewModel(string name, bool isSelected, Action onChanged)
    {
        Name = name;
        _isSelected = isSelected;
        _onChanged = onChanged;
    }

    public string Name { get; }

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (Set(ref _isSelected, value))
            {
                _onChanged();
            }
        }
    }
}
