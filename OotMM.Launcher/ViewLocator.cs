using Avalonia.Controls;
using Avalonia.Controls.Templates;
using OotMM.Launcher.ViewModels;

namespace OotMM.Launcher;

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
            return new TextBlock { Text = $"missing view: {name}" };
        }

        return (Control)Activator.CreateInstance(type)!;
    }

    public bool Match(object? data) => data is ViewModelBase;
}
