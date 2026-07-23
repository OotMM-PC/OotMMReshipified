using System.Diagnostics;
using System.Runtime.InteropServices;

namespace OotMM.Launcher.Session;

/// <summary>Child-process control for a game port. Boot parameters travel via
/// environment variables, matching the contract in the ports' OoTMM patches.</summary>
public sealed class GameProcess : IDisposable
{
    private readonly Process? _process;
    private readonly List<nint> _suspendedThreads = [];
    private bool _suspended;

    public GameProcess(string exePath, string bootConfigPath,
                       bool setupOnly = false, bool setupNeedMq = false, nint parentWindow = 0)
    {
        var start = new ProcessStartInfo(exePath)
        {
            WorkingDirectory = Path.GetDirectoryName(exePath) ?? "",
            UseShellExecute = false,
        };
        if (!string.IsNullOrWhiteSpace(bootConfigPath))
        {
            start.Environment["OOTMM_BOOT_CONFIG"] = bootConfigPath;
        }
        if (setupOnly)
        {
            start.Environment["OOTMM_SETUP_ONLY"] = "1";
        }

        if (setupOnly && setupNeedMq)
        {
            start.Environment["OOTMM_SETUP_NEED_MQ"] = "1";
        }

        if (parentWindow != 0)
        {
            start.Environment["OOTMM_PARENT_HWND"] = parentWindow.ToString();
        }

        try
        {
            _process = Process.Start(start);
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            _process = null;
        }
    }

    public bool Valid => _process is not null;

    public int Pid => _process?.Id ?? 0;

    public bool Running
    {
        get
        {
            try
            {
                return _process is { HasExited: false };
            }
            catch (InvalidOperationException)
            {
                return false;
            }
        }
    }

    public void Terminate()
    {
        Resume();
        try
        {
            if (_process is { HasExited: false })
            {
                _process.Kill();
                _process.WaitForExit(5000);
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
        }
    }

    public bool Suspend()
    {
        if (_suspended || _process is null || !Running)
        {
            return _suspended;
        }

        foreach (ProcessThread thread in _process.Threads)
        {
            var handle = OpenThread(ThreadSuspendResume, false, (uint)thread.Id);
            if (handle == 0 || SuspendThread(handle) == unchecked((uint)-1))
            {
                if (handle != 0)
                {
                    CloseHandle(handle);
                }

                Resume();
                return false;
            }

            _suspendedThreads.Add(handle);
        }

        _suspended = _suspendedThreads.Count > 0;
        return _suspended;
    }

    public void Resume()
    {
        foreach (var handle in _suspendedThreads)
        {
            ResumeThread(handle);
            CloseHandle(handle);
        }

        _suspendedThreads.Clear();
        _suspended = false;
    }

    public void Dispose()
    {
        Resume();
        _process?.Dispose();
    }

    private const uint ThreadSuspendResume = 0x0002;

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern nint OpenThread(uint access, bool inherit, uint threadId);

    [DllImport("kernel32.dll")]
    private static extern uint SuspendThread(nint thread);

    [DllImport("kernel32.dll")]
    private static extern uint ResumeThread(nint thread);

    [DllImport("kernel32.dll")]
    private static extern bool CloseHandle(nint handle);
}
