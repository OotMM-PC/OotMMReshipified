namespace OotMM.Launcher.ViewModels;

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
