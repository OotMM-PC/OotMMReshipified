using System.Runtime.InteropServices;
using System.Text;

namespace OotMM.Launcher;

[Qt.Ignore]
internal sealed class NativeGameHost
{
    private const uint WsChild = 0x40000000;
    private const uint WsVisible = 0x10000000;
    private const uint WsClipSiblings = 0x04000000;
    private const uint WsClipChildren = 0x02000000;
    private const uint SsBlackRect = 0x00000004;
    private const uint SwpNoZOrder = 0x0004;
    private const uint SwpNoActivate = 0x0010;
    private const uint GwOwner = 4;

    private nint _handle;

    public nint Place(int x, int y, int width, int height)
    {
        if (_handle != 0 && !IsWindow(_handle))
        {
            _handle = 0;
        }

        if (_handle == 0)
        {
            var parent = FindLauncherWindow();
            if (parent == 0)
            {
                return 0;
            }

            _handle = CreateWindowExW(0, "STATIC", "",
                WsChild | WsVisible | WsClipSiblings | WsClipChildren | SsBlackRect,
                x, y, Math.Max(1, width), Math.Max(1, height), parent, 0, 0, 0);
            return _handle;
        }

        SetWindowPos(_handle, 0, x, y, Math.Max(1, width), Math.Max(1, height), SwpNoZOrder | SwpNoActivate);
        return _handle;
    }

    public void Destroy()
    {
        if (_handle != 0 && IsWindow(_handle))
        {
            DestroyWindow(_handle);
        }

        _handle = 0;
    }

    private static nint FindLauncherWindow()
    {
        var pid = (uint)Environment.ProcessId;
        nint best = 0;
        long bestArea = 0;
        EnumWindows((hwnd, _) =>
        {
            GetWindowThreadProcessId(hwnd, out var owner);
            if (owner != pid || !IsWindowVisible(hwnd) || GetWindow(hwnd, GwOwner) != 0)
            {
                return true;
            }

            var name = new StringBuilder(128);
            GetClassNameW(hwnd, name, name.Capacity);
            if (!name.ToString().Contains("QWindow", StringComparison.Ordinal))
            {
                return true;
            }

            GetClientRect(hwnd, out var rect);
            var area = (long)(rect.Right - rect.Left) * (rect.Bottom - rect.Top);
            if (area > bestArea)
            {
                best = hwnd;
                bestArea = area;
            }

            return true;
        }, 0);
        return best;
    }

    private delegate bool EnumWindowsProc(nint hwnd, nint lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern nint CreateWindowExW(uint exStyle, string className, string windowName, uint style,
        int x, int y, int width, int height, nint parent, nint menu, nint instance, nint param);

    [DllImport("user32.dll")]
    private static extern bool DestroyWindow(nint hwnd);

    [DllImport("user32.dll")]
    private static extern bool IsWindow(nint hwnd);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(nint hwnd);

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(nint hwnd, nint insertAfter, int x, int y, int cx, int cy, uint flags);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc callback, nint lParam);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(nint hwnd, out uint processId);

    [DllImport("user32.dll")]
    private static extern nint GetWindow(nint hwnd, uint command);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassNameW(nint hwnd, StringBuilder name, int maxCount);

    [DllImport("user32.dll")]
    private static extern bool GetClientRect(nint hwnd, out Rect rect);
}
