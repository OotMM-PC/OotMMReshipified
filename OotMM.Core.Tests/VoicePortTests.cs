using System.IO.Compression;
using System.Text;
using OotMM.Core.Models;
using Xunit;

namespace OotMM.Core.Tests;

public sealed class VoicePortTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "ootmm-voice-" + Guid.NewGuid().ToString("N"));

    public VoicePortTests() => Directory.CreateDirectory(_dir);

    public void Dispose() => Directory.Delete(_dir, true);

    private static void WriteText(BinaryWriter writer, string text)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        writer.Write((uint)bytes.Length);
        writer.Write(bytes);
    }

    private static byte[] Font(params string[] effects)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write(new byte[64]);
        writer.Write(0);
        writer.Write((byte)2);
        writer.Write((byte)1);
        writer.Write(new byte[6]);
        writer.Write(1u);
        writer.Write(1u);
        writer.Write((uint)effects.Length);

        writer.Write(new byte[3]);
        writer.Write(2u);
        writer.Write(new byte[8]);
        writer.Write((byte)1);
        WriteText(writer, "audio/samples/Drum_META");
        writer.Write(1.0f);

        writer.Write(new byte[5]);
        writer.Write(1u);
        writer.Write(new byte[4]);
        writer.Write((byte)0);
        writer.Write((byte)1);
        writer.Write((byte)1);
        WriteText(writer, "audio/samples/Flute_META");
        writer.Write(1.0f);
        writer.Write((byte)0);

        foreach (var effect in effects)
        {
            if (effect.Length == 0)
            {
                writer.Write((byte)0);
                continue;
            }

            writer.Write((byte)1);
            writer.Write((byte)1);
            WriteText(writer, effect);
            writer.Write(0.625f);
        }

        writer.Flush();
        return stream.ToArray();
    }

    private string Archive(string name, params (string Path, string Text)[] entries)
    {
        var path = Path.Combine(_dir, name);
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        foreach (var (entryPath, text) in entries)
        {
            using var stream = zip.CreateEntry(entryPath).Open();
            stream.Write(Encoding.UTF8.GetBytes(text));
        }

        return path;
    }

    [Fact]
    public void SoundEffectSamples_ReadsEverySlotPastDrumsAndInstruments()
    {
        var samples = VoicePort.SoundEffectSamples(Font("audio/samples/A_META", "", "audio/samples/C_META"));

        Assert.Equal(["audio/samples/A_META", "", "audio/samples/C_META"], samples);
    }

    [Fact]
    public void SoundEffectSamples_ReturnsNothingForATruncatedFont()
    {
        var font = Font("audio/samples/A_META", "audio/samples/B_META");

        Assert.Empty(VoicePort.SoundEffectSamples(font[..^6]));
        Assert.Empty(VoicePort.SoundEffectSamples([]));
    }

    [Fact]
    public void MapSlots_PairsSamplesBySlotAndKeepsOnlyThePrefix()
    {
        var oot = Font(
            "audio/samples/Adult Link - Attack 1_META", "audio/samples/Child Link - Attack 1_META",
            "audio/samples/Adult Link - Hup_META", "audio/samples/Navi - Hey!_META");
        var mm = Font(
            "audio/samples/AdultLinkAttack1_META", "audio/samples/SAMPLE_0_193_META",
            "audio/samples/AdultLinkHup_META", "audio/samples/Tatl_META");

        var adult = VoicePort.MapSlots(oot, mm, VoicePort.AdultPrefix);
        var child = VoicePort.MapSlots(oot, mm, VoicePort.ChildPrefix);

        Assert.Equal(2, adult.Count);
        Assert.Equal("audio/samples/AdultLinkAttack1_META", adult["audio/samples/Adult Link - Attack 1_META"]);
        Assert.Equal("audio/samples/AdultLinkHup_META", adult["audio/samples/Adult Link - Hup_META"]);
        Assert.Equal("audio/samples/SAMPLE_0_193_META", Assert.Single(child).Value);
    }

    [Fact]
    public void MapSlots_GivesASharedMmSampleToItsLowestSlot()
    {
        var oot = Font(
            "audio/samples/Adult Link - Painful Landing_META", "audio/samples/Adult Link - Glug_META", "",
            "audio/samples/Adult Link - Refreshed_META");
        var mm = Font(
            "audio/samples/SAMPLE_0_188_META", "audio/samples/SAMPLE_0_188_META",
            "audio/samples/Unused_META", "");

        var map = VoicePort.MapSlots(oot, mm, VoicePort.AdultPrefix);

        Assert.Equal("audio/samples/SAMPLE_0_188_META", Assert.Single(map).Value);
        Assert.Equal("audio/samples/Adult Link - Painful Landing_META", Assert.Single(map).Key);
    }

    [Fact]
    public void SplitSharedSlots_GivesFoldedSlotsTheirOwnSamples()
    {
        var oot = Font(
            "audio/samples/Adult Link - Painful Landing_META", "audio/samples/Adult Link - Glug_META", "",
            "audio/samples/Adult Link - Refreshed_META", "audio/samples/Adult Link - Glug_META");
        var mm = Font(
            "audio/samples/SAMPLE_0_188_META", "audio/samples/SAMPLE_0_188_META", "audio/samples/Unused_META", "",
            "audio/samples/SAMPLE_0_188_META");

        var (font, samples) = VoicePort.SplitSharedSlots(oot, mm, VoicePort.AdultPrefix);

        Assert.Equal("audio/samples/OotLinkVoiceSlot1_META", Assert.Single(samples).Value);
        Assert.Equal("audio/samples/Adult Link - Glug_META", Assert.Single(samples).Key);
        Assert.Equal(
            ["audio/samples/SAMPLE_0_188_META", "audio/samples/OotLinkVoiceSlot1_META", "audio/samples/Unused_META", "",
             "audio/samples/OotLinkVoiceSlot1_META"],
            VoicePort.SoundEffectSamples(font));
    }

    [Fact]
    public void SplitSharedSlots_ReturnsTheSameFontWhenNothingIsFolded()
    {
        var oot = Font("audio/samples/Child Link - Attack 1_META", "audio/samples/Child Link - Hup 1_META");
        var mm = Font("audio/samples/SAMPLE_0_193_META", "audio/samples/SAMPLE_0_197_META");

        var (font, samples) = VoicePort.SplitSharedSlots(oot, mm, VoicePort.ChildPrefix);

        Assert.Empty(samples);
        Assert.Same(mm, font);
    }

    [Fact]
    public void SetEntry_AddsAndReplacesAnEntry()
    {
        var port = Archive("port.o2r", (VoicePort.MmFont, "old"), ("objects/ot_obj_link_boy/model", "model"));

        VoicePort.SetEntry(port, VoicePort.MmFont, Encoding.UTF8.GetBytes("new"));
        VoicePort.SetEntry(port, "audio/samples/Extra_META", Encoding.UTF8.GetBytes("extra"));

        Assert.Equal("new", Encoding.UTF8.GetString(VoicePort.ReadBaseEntry([port], VoicePort.MmFont)!));
        Assert.Equal("extra", Encoding.UTF8.GetString(VoicePort.ReadBaseEntry([port], "audio/samples/Extra_META")!));
        using var zip = ZipFile.OpenRead(port);
        Assert.Equal(3, zip.Entries.Count);
    }

    [Fact]
    public void AddSamples_WritesMappedSamplesUnderMmNamesAndLetsLaterSourcesWin()
    {
        var first = Archive("first.o2r",
            ("audio/samples/Adult Link - Attack 1_META", "first-attack"),
            ("audio/samples/Adult Link - Hup_META", "first-hup"),
            ("audio/samples/Child Link - Attack 1_META", "child"));
        var second = Archive("second.o2r", ("audio/samples/Adult Link - Attack 1_META", "second-attack"));
        var port = Archive("port.o2r", ("objects/ot_obj_link_boy/model", "model"));
        var map = new Dictionary<string, string>
        {
            ["audio/samples/Adult Link - Attack 1_META"] = "audio/samples/AdultLinkAttack1_META",
            ["audio/samples/Adult Link - Hup_META"] = "audio/samples/AdultLinkHup_META",
        };

        Assert.Equal(2, VoicePort.AddSamples([first, second], port, map));

        using var zip = ZipFile.OpenRead(port);
        string Read(string entry)
        {
            using var reader = new StreamReader(zip.GetEntry(entry)!.Open());
            return reader.ReadToEnd();
        }

        Assert.Equal(3, zip.Entries.Count);
        Assert.Equal("second-attack", Read("audio/samples/AdultLinkAttack1_META"));
        Assert.Equal("first-hup", Read("audio/samples/AdultLinkHup_META"));
        Assert.Equal("model", Read("objects/ot_obj_link_boy/model"));
    }

    [Fact]
    public void AddSamples_LeavesThePortAloneWhenNoSourceCarriesAVoice()
    {
        var source = Archive("model.o2r", ("objects/object_link_boy/model", "model"));
        var port = Archive("port.o2r", ("objects/ot_obj_link_boy/model", "model"));
        var before = File.ReadAllBytes(port);
        var map = new Dictionary<string, string>
        {
            ["audio/samples/Adult Link - Hup_META"] = "audio/samples/AdultLinkHup_META",
        };

        Assert.Equal(0, VoicePort.AddSamples([source], port, map));
        Assert.Equal(before, File.ReadAllBytes(port));
    }

    [Fact]
    public void ReadBaseEntry_UsesTheFirstArchiveThatHasTheEntry()
    {
        var missing = Path.Combine(_dir, "missing.o2r");
        var without = Archive("without.o2r", ("other", "x"));
        var with = Archive("with.o2r", (VoicePort.MmFont, "font"));

        Assert.Equal("font", Encoding.UTF8.GetString(VoicePort.ReadBaseEntry([missing, without, with], VoicePort.MmFont)!));
        Assert.Null(VoicePort.ReadBaseEntry([missing, without], VoicePort.MmFont));
    }
}
