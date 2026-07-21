using Avalonia;

namespace OotMM.Launcher;

internal static class Program
{
    // Avalonia requires the app to start on the UI thread with a classic desktop
    // lifetime; everything else (window creation, view models) happens in App.
    [STAThread]
    public static void Main(string[] args) =>
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);

    // Also used by the Avalonia XAML previewer, which is why it must stay public
    // and keep this exact name.
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
                  .UsePlatformDetect()
                  .LogToTrace();
}
