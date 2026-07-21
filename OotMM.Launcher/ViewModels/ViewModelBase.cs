using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace OotMM.Launcher.ViewModels;

/// <summary>
/// Minimal INotifyPropertyChanged base. Hand-rolled instead of pulling in an MVVM
/// framework so the whole binding mechanism stays visible in one small file.
/// </summary>
public abstract class ViewModelBase : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected void Raise([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    /// <summary>Sets the backing field and raises change notification; returns
    /// true only when the value actually changed, so callers can chain side
    /// effects (revalidation, persistence) without firing them on no-ops.</summary>
    protected bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        Raise(name);
        return true;
    }
}
