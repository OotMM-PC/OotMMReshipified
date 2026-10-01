using System.Security.Cryptography;
using System.Text.Json.Nodes;
using OotMM.Core;

namespace OotMM.Core.Tests;

public sealed class NativeBuildCompatibilityTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "ootmm-compat-" + Guid.NewGuid());
    private readonly string _executable;

    public NativeBuildCompatibilityTests()
    {
        Directory.CreateDirectory(_directory);
        _executable = Path.Combine(_directory, "game.exe");
        File.WriteAllBytes(_executable, [1, 2, 3, 4]);
    }

    private JsonObject Manifest(Game game) => new()
    {
        ["schema"] = 1,
        ["bootProtocol"] = 1,
        ["game"] = GameInfo.ToSeedTag(game),
        ["sha256"] = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(_executable))),
        ["capabilities"] = game == Game.Mm
            ? new JsonArray("bound-save-v1", "paired-copy-v1", "shared-resources-v1", "mm-recovery-v1", "native-save-validation-v1")
            : new JsonArray("bound-save-v1", "paired-copy-v1", "shared-resources-v1"),
    };

    private void Write(JsonObject manifest) => File.WriteAllText(_executable + ".ootmm.json", manifest.ToJsonString());

    [Theory]
    [InlineData(Game.Oot)]
    [InlineData(Game.Mm)]
    public void AcceptsMatchingBuildWithRequiredCapabilities(Game game)
    {
        Write(Manifest(game));
        Assert.True(NativeBuildCompatibility.Validate(_executable, game, out var error), error);
        Assert.Empty(error);
    }

    [Theory]
    [InlineData("bound-save-v1")]
    [InlineData("paired-copy-v1")]
    [InlineData("shared-resources-v1")]
    [InlineData("mm-recovery-v1")]
    [InlineData("native-save-validation-v1")]
    public void RejectsBuildMissingRequiredBehavior(string capability)
    {
        var manifest = Manifest(Game.Mm);
        var values = manifest["capabilities"]!.AsArray();
        values.Remove(values.Single(value => value!.GetValue<string>() == capability));
        Write(manifest);
        Assert.False(NativeBuildCompatibility.Validate(_executable, Game.Mm, out _));
    }

    [Fact]
    public void RejectsMissingManifestAndReplacedExecutableWithoutMutatingEither()
    {
        Assert.False(NativeBuildCompatibility.Validate(_executable, Game.Mm, out _));
        Write(Manifest(Game.Mm));
        var manifestBytes = File.ReadAllBytes(_executable + ".ootmm.json");
        File.WriteAllBytes(_executable, [5, 6, 7]);
        Assert.False(NativeBuildCompatibility.Validate(_executable, Game.Mm, out var error));
        Assert.Contains("does not match", error);
        Assert.Equal(manifestBytes, File.ReadAllBytes(_executable + ".ootmm.json"));
        Assert.Equal(new byte[] { 5, 6, 7 }, File.ReadAllBytes(_executable));
        File.Delete(_executable);
        Assert.False(NativeBuildCompatibility.Validate(_executable, Game.Mm, out _));
    }

    [Theory]
    [InlineData("schema", "2")]
    [InlineData("schema", "\"1\"")]
    [InlineData("bootProtocol", "2")]
    [InlineData("game", "\"oot\"")]
    [InlineData("capabilities", "null")]
    [InlineData("capabilities", "[1]")]
    [InlineData("sha256", "null")]
    public void RejectsUnsupportedOrMalformedFields(string field, string value)
    {
        var manifest = Manifest(Game.Mm);
        manifest[field] = JsonNode.Parse(value);
        Write(manifest);
        Assert.False(NativeBuildCompatibility.Validate(_executable, Game.Mm, out _));
    }

    [Fact]
    public void RejectsAmbiguousAndOversizedManifests()
    {
        var manifest = Manifest(Game.Mm);
        manifest["capabilities"]!.AsArray().Add("mm-recovery-v1");
        Write(manifest);
        Assert.False(NativeBuildCompatibility.Validate(_executable, Game.Mm, out _));
        var valid = Manifest(Game.Mm).ToJsonString();
        foreach (var invalid in new[] { "{", "[]", valid.Replace("\"schema\":1", "\"schema\":1,\"schema\":1"), valid + new string(' ', 65536) })
        {
            File.WriteAllText(_executable + ".ootmm.json", invalid);
            Assert.False(NativeBuildCompatibility.Validate(_executable, Game.Mm, out _));
        }
    }

    public void Dispose() => Directory.Delete(_directory, true);
}
