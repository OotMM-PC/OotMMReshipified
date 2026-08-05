using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using OotMM.Launcher.ViewModels;

namespace OotMM.Launcher.Views;

public partial class GameCardView : UserControl
{
    private const double DragThreshold = 5;

    private PointerPressedEventArgs? _pressArgs;
    private Point _pressPoint;
    // A plain press on an already-selected row must not collapse the selection yet: it may be
    // the start of a block drag. The collapse happens on release, only if no drag began.
    private ModEntryViewModel? _pressedItem;
    private bool _collapseOnRelease;
    // True while a drag from THIS card's list is in flight; the drop handlers accept nothing
    // else, so a drag can never land in the other game's list.
    private bool _dragActive;

    public GameCardView()
    {
        InitializeComponent();
        ModsList.AddHandler(PointerPressedEvent, OnListPointerPressed, RoutingStrategies.Tunnel);
        ModsList.AddHandler(PointerMovedEvent, OnListPointerMoved, RoutingStrategies.Tunnel);
        ModsList.AddHandler(PointerReleasedEvent, OnListPointerReleased, RoutingStrategies.Tunnel);
        ModsList.AddHandler(DragDrop.DragOverEvent, OnDragOver);
        ModsList.AddHandler(DragDrop.DropEvent, OnDrop);
    }

    private void OnListPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        // A press on a checkbox or arrow is a click, never a drag handle.
        var onControl = e.Source is Visual source
                        && source.GetSelfAndVisualAncestors().OfType<Control>()
                            .TakeWhile(c => c != ModsList)
                            .Any(c => c is Button or CheckBox);
        _pressArgs = !onControl && e.GetCurrentPoint(ModsList).Properties.IsLeftButtonPressed ? e : null;
        _pressPoint = e.GetPosition(ModsList);

        _pressedItem = null;
        _collapseOnRelease = false;
        if (_pressArgs is not null
            && e.KeyModifiers == KeyModifiers.None
            && e.Source is Visual pressed
            && pressed.GetSelfAndVisualAncestors().OfType<ListBoxItem>().FirstOrDefault()
                is { DataContext: ModEntryViewModel mod }
            && mod.IsSelected
            && DataContext is GameCardViewModel vm
            && vm.Mods.Count(m => m.IsSelected) > 1)
        {
            _pressedItem = mod;
            _collapseOnRelease = true;
            e.Handled = true;
        }
    }

    private void OnListPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        _pressArgs = null;
        if (_collapseOnRelease && _pressedItem is not null && DataContext is GameCardViewModel vm)
        {
            foreach (var mod in vm.Mods)
            {
                mod.IsSelected = ReferenceEquals(mod, _pressedItem);
            }
        }

        _collapseOnRelease = false;
        _pressedItem = null;
    }

    private async void OnListPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_pressArgs is null || !e.GetCurrentPoint(ModsList).Properties.IsLeftButtonPressed)
        {
            return;
        }

        var delta = e.GetPosition(ModsList) - _pressPoint;
        if (Math.Abs(delta.X) < DragThreshold && Math.Abs(delta.Y) < DragThreshold)
        {
            return;
        }

        var pressArgs = _pressArgs;
        _pressArgs = null;
        _collapseOnRelease = false; // it became a drag; the selection survives
        if (DataContext is not GameCardViewModel vm || !vm.Mods.Any(m => m.IsSelected))
        {
            return;
        }

        // The transfer needs content, but identity rides _dragActive, not the payload.
        var payload = new DataTransfer();
        payload.Add(DataTransferItem.CreateText("ootmm-mod-selection"));
        _dragActive = true;
        try
        {
            await DragDrop.DoDragDropAsync(pressArgs, payload, DragDropEffects.Move);
        }
        finally
        {
            _dragActive = false;
        }
    }

    private void OnDragOver(object? sender, DragEventArgs e)
    {
        e.DragEffects = _dragActive ? DragDropEffects.Move : DragDropEffects.None;
        e.Handled = true;
    }

    private void OnDrop(object? sender, DragEventArgs e)
    {
        if (_dragActive && DataContext is GameCardViewModel vm)
        {
            vm.MoveSelectedTo(InsertionIndexAt(e.GetPosition(ModsList).Y, vm.Mods.Count));
            e.Handled = true;
        }
    }

    // Dropping on a row's upper half inserts before it, lower half after it.
    private int InsertionIndexAt(double y, int count)
    {
        for (var i = 0; i < count; i++)
        {
            if (ModsList.ContainerFromIndex(i) is { } container
                && container.TranslatePoint(new Point(0, 0), ModsList) is { } topLeft
                && y < topLeft.Y + container.Bounds.Height / 2)
            {
                return i;
            }
        }

        return count;
    }
}
