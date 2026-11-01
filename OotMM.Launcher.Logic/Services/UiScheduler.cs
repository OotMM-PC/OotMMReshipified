using System.Collections.Concurrent;
using System.Diagnostics;

namespace OotMM.Launcher.Services;

public static class UiScheduler
{
    private static readonly ConcurrentQueue<Action> Posted = new();
    private static readonly List<UiTimer> Timers = [];
    private static readonly Stopwatch Clock = Stopwatch.StartNew();

    public static void Post(Action action) => Posted.Enqueue(action);

    public static UiTimer CreateTimer(TimeSpan interval, Action tick)
    {
        var timer = new UiTimer(interval, tick);
        lock (Timers)
        {
            Timers.Add(timer);
        }

        return timer;
    }

    public static void Pump()
    {
        while (Posted.TryDequeue(out var action))
        {
            action();
        }

        UiTimer[] due;
        var now = Clock.Elapsed;
        lock (Timers)
        {
            Timers.RemoveAll(timer => timer.Disposed);
            due = Timers.Where(timer => timer.IsEnabled && timer.DueAt <= now).ToArray();
        }

        foreach (var timer in due)
        {
            if (!timer.IsEnabled)
            {
                continue;
            }

            timer.DueAt = now + timer.Interval;
            timer.Fire();
        }
    }

    internal static TimeSpan Now => Clock.Elapsed;
}

public sealed class UiTimer : IDisposable
{
    private readonly Action _tick;

    internal UiTimer(TimeSpan interval, Action tick)
    {
        Interval = interval;
        _tick = tick;
    }

    public TimeSpan Interval { get; set; }

    public bool IsEnabled { get; private set; }

    internal TimeSpan DueAt { get; set; }

    internal bool Disposed { get; private set; }

    public void Start()
    {
        if (Disposed)
        {
            return;
        }

        DueAt = UiScheduler.Now + Interval;
        IsEnabled = true;
    }

    public void Stop() => IsEnabled = false;

    public void Dispose()
    {
        IsEnabled = false;
        Disposed = true;
    }

    internal void Fire() => _tick();
}
