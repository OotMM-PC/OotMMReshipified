using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using OotMM.Launcher.ViewModels;

namespace OotMM.Launcher.Views;

public partial class OptionsTabView : UserControl
{
    public OptionsTabView()
    {
        InitializeComponent();
        ScaleSlider.AddHandler(Thumb.DragCompletedEvent,
            (_, _) => (DataContext as OptionsTabViewModel)?.CommitUiScale());
        ScaleSlider.PointerCaptureLost +=
            (_, _) => (DataContext as OptionsTabViewModel)?.CommitUiScale();
        ScaleSlider.KeyUp += (_, _) => (DataContext as OptionsTabViewModel)?.CommitUiScale();
    }
}
