using Avalonia.Controls;
using OotMM.Launcher.ViewModels;

namespace OotMM.Launcher.Views;

public partial class RuntimeView : UserControl
{
    public RuntimeView()
    {
        InitializeComponent();
        GameHost.HandleCreated += handle =>
            (DataContext as RuntimeViewModel)?.NotifyGameHostCreated(handle);
    }
}
