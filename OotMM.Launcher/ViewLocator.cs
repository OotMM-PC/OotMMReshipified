using Avalonia.Controls;
using Avalonia.Controls.Templates;
using OotMM.Launcher.ViewModels;

namespace OotMM.Launcher;

/// <summary>
/// Maps view models to views by naming convention:
/// OotMM.Launcher.ViewModels.FooViewModel -> OotMM.Launcher.Views.FooView.
/// Registered once in App.axaml, so any ContentControl bound to a view model
/// renders the matching view without per-screen wiring.
/// </summary>
public class ViewLocator : IDataTemplate
{
    public Control Build(object? data)
    {
        if (data is null)
        {
            return new TextBlock { Text = "null view model" };
        }

        var name = data.GetType().FullName!
            .Replace(".ViewModels.", ".Views.", StringComparison.Ordinal)
            .Replace("ViewModel", "View", StringComparison.Ordinal);
        var type = Type.GetType(name);
        if (type is null)
        {
            // A missing view is a programming error; surface it loudly in the UI
            // instead of rendering an empty region that is easy to miss.
            return new TextBlock { Text = $"missing view: {name}" };
        }

        return (Control)Activator.CreateInstance(type)!;
    }

    public bool Match(object? data) => data is ViewModelBase;
}
