using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using OotMM.Launcher.Models;
using OotMM.Launcher.Services;
using OotMM.Launcher.ViewModels;
using OotMM.Launcher.Views;

namespace OotMM.Launcher;

public class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var settings = UiSettings.Load();
            var window = new MainWindow();
            var viewModel = new MainWindowViewModel(settings, new StorageFilePicker(window));
            window.DataContext = viewModel;
            desktop.MainWindow = window;
        }

        base.OnFrameworkInitializationCompleted();
    }

    public static void ApplyAccent(bool violet)
    {
        var app = Current;
        if (app is null)
        {
            return;
        }

        var main = violet ? Color.Parse("#A97FE0") : Color.Parse("#E0B84C");
        var bright = violet ? Color.Parse("#C7A6F2") : Color.Parse("#F2D27C");

        app.Resources["AccentBrush"] = new SolidColorBrush(main);
        app.Resources["AccentBrightBrush"] = new SolidColorBrush(bright);
        app.Resources["AccentDimBrush"] = new SolidColorBrush(main, 0.35);
        app.Resources["AccentFaintBrush"] = new SolidColorBrush(main, 0.15);
    }
}
