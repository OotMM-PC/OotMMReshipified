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
    public void PvpDamageSavesAndStaysInRange()
    {
        var settings = new UiSettings { EnablePvp = true, PvpDamagePercent = 200 };
        var config = new ConfigViewModel(settings, new NoPicker(), () => { }, (_, _, _, _) => { });
        Assert.Equal(200, config.HostPvpDamage);

        config.HostPvpDamage = 50;
        Assert.Equal(50, settings.PvpDamagePercent);

        config.HostPvpDamage = 5000;
        Assert.Equal(1000, config.HostPvpDamage);
    }

    [Fact]
    public void HostRulesRestoreFromSettings()
    {
        var settings = new UiSettings { EnablePvp = true, EnableFriendlyFire = true, DeathLink = Core.Relay.DeathLinkMode.Team };
        var config = new ConfigViewModel(settings, new NoPicker(), () => { }, (_, _, _, _) => { });

        Assert.True(config.HostPvp);
        Assert.True(config.HostFriendlyFire);
        Assert.Equal(1, config.HostDeathLink);
    }

    [Theory]
    [InlineData("{}", Core.Relay.DeathLinkMode.Off)]
    [InlineData("{\"EnableDeathLink\":false}", Core.Relay.DeathLinkMode.Off)]
    [InlineData("{\"EnableDeathLink\":true}", Core.Relay.DeathLinkMode.Global)]
    [InlineData("{\"DeathLink\":1}", Core.Relay.DeathLinkMode.Team)]
    public void DeathLinkSettingLoadsFromCurrentAndOlderFiles(string json, Core.Relay.DeathLinkMode expected)
    {
        var settings = System.Text.Json.JsonSerializer.Deserialize<UiSettings>(json)!;
        Assert.Equal(expected, settings.DeathLink);
        Assert.DoesNotContain("EnableDeathLink", System.Text.Json.JsonSerializer.Serialize(settings));
    }

    private sealed class NoPicker : IFilePicker
    {
        public Task<string?> PickFileAsync(string title, string filterName, params string[] patterns) =>
            Task.FromResult<string?>(null);
    }
}
