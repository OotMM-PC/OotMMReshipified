using Avalonia.Controls;
using Avalonia.Media;
using OotMM.Launcher.ViewModels;

namespace OotMM.Launcher.Views;

public partial class RuntimeView : UserControl
{
    public RuntimeView()
    {
        InitializeComponent();
        GameHost.HandleCreated += handle =>
            (DataContext as RuntimeViewModel)?.NotifyGameHostCreated(handle);
        DataContextChanged += (_, _) =>
        {
            if (DataContext is not RuntimeViewModel runtime)
            {
                return;
            }

            ApplyScale(runtime);
            runtime.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(RuntimeViewModel.SidebarScale))
                {
                    ApplyScale(runtime);
                }
            };
        };
    }

    private void ApplyScale(RuntimeViewModel runtime) =>
        SidebarScaleHost.LayoutTransform = new ScaleTransform(runtime.SidebarScale, runtime.SidebarScale);
}
