using System.ComponentModel;
using System.Runtime.CompilerServices;
using OotMM.Launcher.Models;
using OotMM.Launcher.Services;
using OotMM.Launcher.ViewModels;

namespace OotMM.Launcher;

public sealed class Backend : INotifyPropertyChanged
{
    private readonly QmlFilePicker _picker;
    private readonly NativeGameHost _gameHost = new();
    private RuntimeViewModel? _hostedRuntime;

    public Backend()
    {
        _picker = new QmlFilePicker(this);
        Main = new MainWindowViewModel(UiSettings.Load(), _picker);
        Main.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MainWindowViewModel.IsRuntime) && !Main.IsRuntime)
            {
                _gameHost.Destroy();
                _hostedRuntime = null;
            }
        };
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public MainWindowViewModel Main { get; }

    public int PickRequest { get; private set; }

    public string PickTitle { get; private set; } = "";

    public string PickFilter { get; private set; } = "";

    public void Pump() => UiScheduler.Pump();

    public void WindowActivated() => Main.OnWindowActivated();

    public void CompletePick(string path) => _picker.Complete(path);

    public void SetTextFieldFocus(bool focused)
    {
        if (Main.IsRuntime)
        {
            Main.Runtime?.SetLauncherKeyboard(focused);
        }
    }

    public void SetGameArea(double x, double y, double width, double height, double devicePixelRatio)
    {
        if (!Main.IsRuntime || Main.Runtime is not { IsPreview: false } runtime)
        {
            return;
        }

        var handle = _gameHost.Place(
            (int)Math.Round(x * devicePixelRatio), (int)Math.Round(y * devicePixelRatio),
            (int)Math.Round(width * devicePixelRatio), (int)Math.Round(height * devicePixelRatio));
        if (handle != 0 && _hostedRuntime != runtime)
        {
            _hostedRuntime = runtime;
            runtime.NotifyGameHostCreated(handle);
        }
    }

    internal void RequestPick(string title, string filter)
    {
        PickTitle = title;
        PickFilter = filter;
        PickRequest++;
        Raise(nameof(PickTitle));
        Raise(nameof(PickFilter));
        Raise(nameof(PickRequest));
    }

    private void Raise([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
