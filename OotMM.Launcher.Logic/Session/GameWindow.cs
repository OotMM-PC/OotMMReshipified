using System.Runtime.InteropServices;

namespace OotMM.Launcher.Session;

public static class GameWindow
{
    public static nint FindGameChild(nint host, int pid)
    {
        if (host == 0 || pid == 0)
        {
            return 0;
        }

        var found = (nint)0;
        EnumChildWindows(host, (child, lparam) =>
        {
            GetWindowThreadProcessId(child, out var childPid);
            if (childPid != (uint)pid)
            {
                return true;
            }

            found = child;
            return false;
        }, 0);
        return found;
    }

    public static bool IsAlive(nint window) => window != 0 && IsWindow(window);

    public static (int Width, int Height) ClientSize(nint window)
    {
        if (!IsAlive(window) || !GetClientRect(window, out var rect))
        {
            return (0, 0);
        }

        return (rect.Right - rect.Left, rect.Bottom - rect.Top);
    }

    public static bool CursorOverWindow(nint window)
    {
        if (!IsAlive(window) || !GetCursorPos(out var point)
            || !ScreenToClient(window, ref point) || !GetClientRect(window, out var rect))
        {
            return false;
        }

        return point.X >= rect.Left && point.X < rect.Right
               && point.Y >= rect.Top && point.Y < rect.Bottom;
    }

    public static bool OwnerIsForeground(nint window) =>
        IsAlive(window) && GetForegroundWindow() == GetAncestor(window, 2);

    public static bool LeftButtonDown() => (GetAsyncKeyState(0x01) & 0x8000) != 0;

    public static void Layout(nint child, int width, int height) =>
        MoveWindow(child, 0, 0, Math.Max(width, 0), Math.Max(height, 0), true);

    public static void Focus(nint child)
    {
        if (!IsAlive(child))
        {
            return;
        }

        var childThread = GetWindowThreadProcessId(child, out _);
        var selfThread = GetCurrentThreadId();
        if (childThread != selfThread)
        {
            AttachThreadInput(selfThread, childThread, true);
        }

        SetFocus(child);
        if (childThread != selfThread)
        {
            AttachThreadInput(selfThread, childThread, false);
        }
    }

    public static void PostEscape(nint child)
    {
        if (!IsAlive(child))
        {
            return;
        }

        Focus(child);
        PostMessageW(child, 0x0100, 0x1B, 0x00010001);
        PostMessageW(child, 0x0101, 0x1B, unchecked((nint)0xC0010001));
    }

    private delegate bool EnumChildProc(nint hwnd, nint lparam);

    [DllImport("user32.dll")]
    private static extern bool EnumChildWindows(nint parent, EnumChildProc callback, nint lparam);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(nint hwnd, out uint pid);

    [DllImport("user32.dll")]
    private static extern bool IsWindow(nint hwnd);

    private struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    private struct Point
    {
        public int X;
        public int Y;
    }

    [DllImport("user32.dll")]
    private static extern bool GetClientRect(nint hwnd, out Rect rect);

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out Point point);

    [DllImport("user32.dll")]
    private static extern bool ScreenToClient(nint hwnd, ref Point point);

    [DllImport("user32.dll")]
    private static extern nint GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern nint GetAncestor(nint hwnd, uint flags);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int key);

    [DllImport("user32.dll")]
    private static extern bool MoveWindow(nint hwnd, int x, int y, int width, int height, bool repaint);

    [DllImport("user32.dll")]
    private static extern nint SetFocus(nint hwnd);

    [DllImport("user32.dll")]
    private static extern bool AttachThreadInput(uint attach, uint attachTo, bool attaching);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [DllImport("user32.dll")]
    private static extern bool PostMessageW(nint hwnd, uint message, nint wparam, nint lparam);
}
