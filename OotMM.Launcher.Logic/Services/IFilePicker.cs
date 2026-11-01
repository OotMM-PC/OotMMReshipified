namespace OotMM.Launcher.Services;

public interface IFilePicker
{
    Task<string?> PickFileAsync(string title, string filterName, params string[] patterns);
}
