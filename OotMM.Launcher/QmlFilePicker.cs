using OotMM.Launcher.Services;

namespace OotMM.Launcher;

[Qt.Ignore]
internal sealed class QmlFilePicker(Backend backend) : IFilePicker
{
    private TaskCompletionSource<string?>? _pending;

    public Task<string?> PickFileAsync(string title, string filterName, params string[] patterns)
    {
        _pending?.TrySetResult(null);
        _pending = new TaskCompletionSource<string?>();
        backend.RequestPick(title, $"{filterName} ({string.Join(" ", patterns)})");
        return _pending.Task;
    }

    public void Complete(string path)
    {
        var pending = _pending;
        _pending = null;
        pending?.TrySetResult(string.IsNullOrWhiteSpace(path) ? null : path);
    }
}
