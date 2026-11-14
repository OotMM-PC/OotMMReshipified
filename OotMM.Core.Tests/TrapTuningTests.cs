using System.Text.Json;
using OotMM.Core;
using Xunit;

namespace OotMM.Core.Tests;

public class TrapTuningTests
{
    [Fact]
    public void ParsesChanceDistancePairs()
    {
        Assert.True(TrapTuning.TryParseKnockback("[25%: 200, 50:60-120; 5:0]", out var rows, out var error), error);
        Assert.Equal([new KnockbackChance(25, 200, 200), new KnockbackChance(50, 60, 120), new KnockbackChance(5, 0, 0)],
                     rows);
    }

    [Fact]
    public void DefaultsSpellOutTheOriginalTraps()
    {
        var tuning = new TrapTuning().Normalized();
        Assert.Equal(TrapTuning.OriginalKnockback, tuning.Knockback);
        Assert.Equal([new KnockbackChance(100, 0, 600)], tuning.KnockbackChances);
        Assert.Equal(100, tuning.IceDamagePercent);
        Assert.True(tuning.IsOriginal);
        Assert.True(new TrapTuning { Knockback = "" }.IsOriginal);
    }

    [Theory]
    [InlineData("60:100, 50:20", "more than 100%")]
    [InlineData("200", "chance:distance")]
    [InlineData("0:100", "1 to 100")]
    [InlineData("10:5000", "0 to 2000")]
    [InlineData("ten:100", "1 to 100")]
    [InlineData("50:300-100", "smaller distance")]
    [InlineData("  ", "at least one")]
    public void RejectsBadKnockback(string text, string expected)
    {
        Assert.False(TrapTuning.TryParseKnockback(text, out _, out var error));
        Assert.Contains(expected, error);
    }

    [Fact]
    public void NormalizedClampsAndDropsInvalidKnockback()
    {
        var tuning = new TrapTuning
        {
            IceBreakoutPercent = 5000,
            IceDamagePercent = -3,
            ShockDamagePercent = 5000,
            FireSeconds = 99,
            ShockSeconds = 0,
            Knockback = "70:10, 70:10",
        }.Normalized();

        Assert.Equal(TrapTuning.MaxIceBreakoutPercent, tuning.IceBreakoutPercent);
        Assert.Equal(0, tuning.IceDamagePercent);
        Assert.Equal(TrapTuning.MaxDamagePercent, tuning.ShockDamagePercent);
        Assert.Equal(TrapTuning.MaxFireSeconds, tuning.FireSeconds);
        Assert.Equal(TrapTuning.MinShockSeconds, tuning.ShockSeconds);
        Assert.Equal(TrapTuning.OriginalKnockback, tuning.Knockback);
    }

    [Fact]
    public void RelaySessionMetaCarriesTheHostTraps()
    {
        var directory = Path.Combine(Path.GetTempPath(), "ootmm-trap-meta-" + Guid.NewGuid());
        Directory.CreateDirectory(directory);
        try
        {
            var rules = new OotMM.Core.Relay.RelayRules
            {
                Traps = new TrapTuning
                {
                    IceBreakoutPercent = 250, FireDamagePercent = 300, ShockSeconds = 3.5, Knockback = "25:200, 50:60-90",
                },
            };
            var engine = new OotMM.Core.Relay.RelayEngine(directory, rules);
            var line = (string)typeof(OotMM.Core.Relay.RelayEngine)
                .GetMethod("SessionMetaLine", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
                .Invoke(engine, [])!;

            var parsed = TrapTuning.FromMetaLine(line.Trim());
            Assert.Equal(250, parsed.IceBreakoutPercent);
            Assert.Equal(300, parsed.FireDamagePercent);
            Assert.Equal(3.5, parsed.ShockSeconds);
            Assert.Equal("25:200, 50:60-90", parsed.Knockback);
            Assert.False(parsed.IsOriginal);
            Assert.True(TrapTuning.FromMetaLine("{\"type\":\"OOTMM_SESSION_META\",\"pvp\":false}").IsOriginal);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public void SettingsSnapshotDefaultsMatchOriginalTraps()
    {
        var settings = new RosterMeta(true, false, false, 100, false, "healthy", 0);

        using var document = JsonDocument.Parse(GameIpcProtocol.BuildSettingsSnapshot(1, settings));
        var traps = document.RootElement.GetProperty("settings").GetProperty("traps");
        Assert.Equal(100, traps.GetProperty("iceBreakoutPercent").GetInt32());
        Assert.Equal(100, traps.GetProperty("iceDamagePercent").GetInt32());
        Assert.Equal(100, traps.GetProperty("fireDamagePercent").GetInt32());
        Assert.Equal(100, traps.GetProperty("shockDamagePercent").GetInt32());
        Assert.Equal(200, traps.GetProperty("fireDuration").GetInt32());
        Assert.Equal(20, traps.GetProperty("shockDuration").GetInt32());
        var row = Assert.Single(traps.GetProperty("knockback").EnumerateArray());
        Assert.Equal(100, row.GetProperty("percent").GetInt32());
        Assert.Equal(0, row.GetProperty("minDistance").GetInt32());
        Assert.Equal(600, row.GetProperty("maxDistance").GetInt32());
    }

    [Fact]
    public void SettingsSnapshotCarriesTunedTraps()
    {
        var tuning = new TrapTuning
        {
            IceBreakoutPercent = 300,
            IceDamagePercent = 250,
            FireDamagePercent = 0,
            FireSeconds = 2.5,
            ShockDamagePercent = 400,
            ShockSeconds = 4,
            Knockback = "25:200",
        };
        var settings = new RosterMeta(true, false, false, 100, false, "healthy", 0, Traps: tuning);

        using var document = JsonDocument.Parse(GameIpcProtocol.BuildSettingsSnapshot(2, settings));
        var traps = document.RootElement.GetProperty("settings").GetProperty("traps");
        Assert.Equal(300, traps.GetProperty("iceBreakoutPercent").GetInt32());
        Assert.Equal(250, traps.GetProperty("iceDamagePercent").GetInt32());
        Assert.Equal(0, traps.GetProperty("fireDamagePercent").GetInt32());
        Assert.Equal(50, traps.GetProperty("fireDuration").GetInt32());
        Assert.Equal(400, traps.GetProperty("shockDamagePercent").GetInt32());
        Assert.Equal(80, traps.GetProperty("shockDuration").GetInt32());
        var row = Assert.Single(traps.GetProperty("knockback").EnumerateArray());
        Assert.Equal(25, row.GetProperty("percent").GetInt32());
        Assert.Equal(200, row.GetProperty("minDistance").GetInt32());
        Assert.Equal(200, row.GetProperty("maxDistance").GetInt32());
    }
}
