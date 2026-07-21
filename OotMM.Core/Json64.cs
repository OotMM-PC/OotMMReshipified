using System.Text.Json;

namespace OotMM.Core;

/// <summary>Core JSON convention: u64s cross the wire as decimal strings
/// (double-safe); readers accept both forms.</summary>
public static class Json64
{
    public static ulong? ReadU64(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Number)
        {
            return element.TryGetUInt64(out var fromNumber) ? fromNumber : null;
        }

        if (element.ValueKind == JsonValueKind.String)
        {
            var text = element.GetString();
            return ulong.TryParse(text, out var fromString) ? fromString : null;
        }

        return null;
    }

    public static string WriteU64(ulong value) => value.ToString();
}
