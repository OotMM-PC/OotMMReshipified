using Avalonia.Controls;
using Avalonia.Platform.Storage;

namespace OotMM.Launcher.Services;

public interface IFilePicker
{
    Task<string?> PickFileAsync(string title, string filterName, params string[] patterns);
}

public sealed class StorageFilePicker(TopLevel topLevel) : IFilePicker
{
    public async Task<string?> PickFileAsync(string title, string filterName, params string[] patterns)
    {
        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType(filterName) { Patterns = patterns },
                FilePickerFileTypes.All,
            ],
        });

        return files.Count > 0 ? files[0].TryGetLocalPath() : null;
    }
}
