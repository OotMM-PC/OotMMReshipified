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
            // The file picker needs a live TopLevel for StorageProvider, so it is
            // created against the window rather than injected earlier.
            var viewModel = new MainWindowViewModel(settings, new StorageFilePicker(window));
            window.DataContext = viewModel;
            desktop.MainWindow = window;
        }

        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>
    /// Swaps the application-wide accent between OoT gold and MM violet. All accent
    /// use sites bind with DynamicResource, so replacing the brushes restyles the
    /// whole window in place — mirrors the old launcher's CurrentAccent behavior.
    /// </summary>
    public static void ApplyAccent(bool violet)
    {
        var app = Current;
        if (app is null)
        {
            return;
        }

        // Gold: warm triforce yellow. Violet: Majora twilight.
        var main = violet ? Color.Parse("#A97FE0") : Color.Parse("#E0B84C");
        var bright = violet ? Color.Parse("#C7A6F2") : Color.Parse("#F2D27C");

        app.Resources["AccentBrush"] = new SolidColorBrush(main);
        app.Resources["AccentBrightBrush"] = new SolidColorBrush(bright);
        app.Resources["AccentDimBrush"] = new SolidColorBrush(main, 0.35);
        app.Resources["AccentFaintBrush"] = new SolidColorBrush(main, 0.15);
    }
}
