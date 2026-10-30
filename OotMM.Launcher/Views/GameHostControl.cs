using Avalonia.Controls;
using Avalonia.Platform;

namespace OotMM.Launcher.Views;

public sealed class GameHostControl : NativeControlHost
{
    public nint Handle { get; private set; }

    public event Action<nint>? HandleCreated;

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
}
