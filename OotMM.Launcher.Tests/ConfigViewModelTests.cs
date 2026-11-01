using OotMM.Launcher.Models;
using OotMM.Launcher.Services;
using OotMM.Launcher.ViewModels;

namespace OotMM.Launcher.Tests;

public sealed class ConfigViewModelTests
{
    [Fact]
    public void FriendlyFireRequiresPvp()
    {
        var settings = new UiSettings();
        var config = new ConfigViewModel(settings, new NoPicker(), () => { }, (_, _, _, _) => { });

        config.HostFriendlyFire = true;
        Assert.False(config.HostFriendlyFire);

        config.HostPvp = true;
        config.HostFriendlyFire = true;
        Assert.True(settings.EnableFriendlyFire);

        config.HostPvp = false;
        Assert.False(config.HostFriendlyFire);
        Assert.False(settings.EnableFriendlyFire);
    }

    [Fact]
    public void HostRulesRestoreFromSettings()
    {
        var settings = new UiSettings { EnablePvp = true, EnableFriendlyFire = true, EnableDeathLink = true };
        var config = new ConfigViewModel(settings, new NoPicker(), () => { }, (_, _, _, _) => { });

        Assert.True(config.HostPvp);
        Assert.True(config.HostFriendlyFire);
        Assert.True(config.HostDeathLink);
    }

    private sealed class NoPicker : IFilePicker
    {
        public Task<string?> PickFileAsync(string title, string filterName, params string[] patterns) =>
            Task.FromResult<string?>(null);
    }
}
