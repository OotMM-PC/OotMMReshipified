using System.Text.Json;

namespace OotMM.Core;

public enum NativeSaveOperation
{
    Select,
    Copy,
}

public sealed record NativeSaveRequest(
    ulong RequestId, NativeSaveOperation Operation, string SaveId, uint ActiveSlot, uint SourceSlot, uint TargetSlot)
{
    public const ulong MaximumRequestId = 9_007_199_254_740_991;

    public static bool TryParse(string json, out NativeSaveRequest? request, out string error)
    {
        request = null;
        error = "Invalid native save request.";
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                return false;
            var properties = root.EnumerateObject().ToArray();
            if (properties.Length != 8 || properties.Select(p => p.Name).Distinct(StringComparer.Ordinal).Count() != 8
                || !root.TryGetProperty("type", out var type) || type.ValueKind != JsonValueKind.String
                || type.GetString() != "nativeSaveRequest"
                || !root.TryGetProperty("version", out var version) || version.ValueKind != JsonValueKind.Number
                || !version.TryGetUInt32(out var protocol) || protocol != 1
                || !root.TryGetProperty("requestId", out var id) || id.ValueKind != JsonValueKind.Number
                || !id.TryGetUInt64(out var requestId) || requestId == 0 || requestId > MaximumRequestId
                || !root.TryGetProperty("operation", out var operation) || operation.ValueKind != JsonValueKind.String
                || !root.TryGetProperty("saveId", out var save) || save.ValueKind != JsonValueKind.String
                || !root.TryGetProperty("activeSlot", out var active) || active.ValueKind != JsonValueKind.Number
                || !active.TryGetUInt32(out var activeSlot) || activeSlot > 2
                || !root.TryGetProperty("sourceSlot", out var source) || source.ValueKind != JsonValueKind.Number
                || !source.TryGetUInt32(out var sourceSlot) || sourceSlot > 2
                || !root.TryGetProperty("targetSlot", out var target) || target.ValueKind != JsonValueKind.Number
                || !target.TryGetUInt32(out var targetSlot) || targetSlot > 2)
                return false;
            var saveId = save.GetString()!;
            var kind = operation.GetString() switch
            {
                "select" => NativeSaveOperation.Select,
                "copy" => NativeSaveOperation.Copy,
                _ => (NativeSaveOperation?)null,
            };
            if (kind is null || string.IsNullOrWhiteSpace(saveId) || saveId.Length > 128
                || saveId.Any(char.IsControl) || kind == NativeSaveOperation.Copy && sourceSlot == targetSlot)
                return false;
            request = new NativeSaveRequest(requestId, kind.Value, saveId, activeSlot, sourceSlot, targetSlot);
            error = "";
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    public bool MatchesActiveSave(string saveId, uint? slot, ulong lastAcceptedRequestId) =>
        SaveId == saveId && ActiveSlot == slot && RequestId > lastAcceptedRequestId;
}
