using Avalonia.Controls;
using OotMM.Launcher.ViewModels;

namespace OotMM.Launcher.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        Activated += (_, _) => (DataContext as MainWindowViewModel)?.OnWindowActivated();
    }
}
