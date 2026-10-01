using System.Text;
using System.Text.Json;

namespace OotMM.Core;

public sealed record MmRecoveryRequest(string Backup, string? PrimaryHex)
{
    public string Serialize() => JsonSerializer.Serialize(new { version = 1, backup = Backup, primaryHex = PrimaryHex });

    public static bool TryParse(string text, out MmRecoveryRequest? request)
    {
        request = null;
        try
        {
            using var document = JsonDocument.Parse(text);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || root.EnumerateObject().Count() != 3
                || !NativeSaveCopyPayload.HasUniqueFields(root)
                || !root.TryGetProperty("version", out var version) || version.ValueKind != JsonValueKind.Number
                || !version.TryGetInt32(out var number) || number != 1
                || !root.TryGetProperty("backup", out var backup) || backup.ValueKind != JsonValueKind.String
                || !root.TryGetProperty("primaryHex", out var primary)
                || primary.ValueKind is not (JsonValueKind.Null or JsonValueKind.String))
                return false;
            var contents = backup.GetString()!;
            var hex = primary.ValueKind == JsonValueKind.Null ? null : primary.GetString();
            if (contents.Length == 0 || Encoding.UTF8.GetByteCount(contents) > 8 * 1024 * 1024
                || (hex is not null && (hex.Length > 16 * 1024 * 1024 || hex.Length % 2 != 0
                    || hex.Any(c => !((c >= '0' && c <= '9') || (c >= 'a' && c <= 'f'))))))
                return false;
            request = new MmRecoveryRequest(contents, hex);
            return true;
        }
        catch (JsonException) { return false; }
    }
}
