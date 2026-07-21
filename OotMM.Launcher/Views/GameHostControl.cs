using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform;

namespace OotMM.Launcher.Views;

/// <summary>Native child window the game render window is parented into
/// (via OOTMM_PARENT_HWND). Reports its handle and pixel size to the owner.</summary>
public sealed class GameHostControl : NativeControlHost
{
    public nint Handle { get; private set; }

    public event Action<nint>? HandleCreated;
    public event Action<PixelSize>? PixelSizeChanged;

    protected override IPlatformHandle CreateNativeControlCore(IPlatformHandle parent)
    {
        var handle = base.CreateNativeControlCore(parent);
        Handle = handle.Handle;
        HandleCreated?.Invoke(Handle);
        return handle;
    }

    protected override void DestroyNativeControlCore(IPlatformHandle control)
    {
        Handle = 0;
        base.DestroyNativeControlCore(control);
    }

    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        base.OnSizeChanged(e);
        PixelSizeChanged?.Invoke(CurrentPixelSize());
    }

    public PixelSize CurrentPixelSize()
    {
        var scaling = TopLevel.GetTopLevel(this)?.RenderScaling ?? 1.0;
        return new PixelSize((int)(Bounds.Width * scaling), (int)(Bounds.Height * scaling));
    }
}
