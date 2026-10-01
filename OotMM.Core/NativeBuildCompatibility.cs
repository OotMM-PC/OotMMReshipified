using System.Security.Cryptography;
using System.Text.Json;

namespace OotMM.Core;

public static class NativeBuildCompatibility
{
    public static bool Validate(string executable, Game game, out string error, params string[] additionalCapabilities)
    {
        error = "This game build is incompatible. Install the matching OoTMM executable and its .ootmm.json manifest.";
        try
        {
            using var manifest = File.OpenRead(executable + ".ootmm.json");
            if (manifest.Length > 65536) return false;
            using var document = JsonDocument.Parse(manifest);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || root.EnumerateObject().Count() != 5
                || !NativeSaveCopyPayload.HasUniqueFields(root)
                || !root.TryGetProperty("schema", out var schema) || schema.ValueKind != JsonValueKind.Number
                || !schema.TryGetInt32(out var schemaNumber) || schemaNumber != 1
                || !root.TryGetProperty("bootProtocol", out var protocol) || protocol.ValueKind != JsonValueKind.Number
                || !protocol.TryGetInt32(out var protocolNumber) || protocolNumber != 1
                || !root.TryGetProperty("game", out var declaredGame) || declaredGame.ValueKind != JsonValueKind.String
                || declaredGame.GetString() != GameInfo.ToSeedTag(game)
                || !root.TryGetProperty("sha256", out var sha) || sha.ValueKind != JsonValueKind.String
                || !root.TryGetProperty("capabilities", out var values) || values.ValueKind != JsonValueKind.Array
                || values.GetArrayLength() > 32)
                return false;
            var capabilities = new HashSet<string>(StringComparer.Ordinal);
            foreach (var value in values.EnumerateArray())
            {
                if (value.ValueKind != JsonValueKind.String || value.GetString() is not { Length: > 0 and <= 64 } name
                    || !capabilities.Add(name))
                    return false;
            }
            var required = new List<string> { "bound-save-v1", "paired-copy-v1", "shared-resources-v1" };
            required.AddRange(additionalCapabilities);
            if (game == Game.Mm)
                required.AddRange(["mm-recovery-v1", "native-save-validation-v1"]);
            if (required.Any(name => !capabilities.Contains(name))) return false;
            using var binary = File.OpenRead(executable);
            if (Convert.ToHexStringLower(SHA256.HashData(binary)) != sha.GetString())
            {
                error = "The game executable does not match its compatibility manifest. Reinstall the matching OoTMM build.";
                return false;
            }
            error = "";
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            error += " " + ex.Message;
            return false;
        }
    }
}
