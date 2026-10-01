using System.Collections.Frozen;
using System.Text.Json;

namespace OotMM.Core;

public sealed class SharedResourceSnapshot
{
    public const int CurrentVersion = 1;
    public const ulong MaxRevision = 9_007_199_254_740_991;

    private SharedResourceSnapshot(string saveId, uint slot, ulong revision, Game source,
        Dictionary<string, uint> values, Dictionary<string, uint> applied)
    {
        SaveId = saveId;
        Slot = slot;
        Revision = revision;
        Source = source;
        Values = values.ToFrozenDictionary(StringComparer.Ordinal);
        Applied = applied.ToFrozenDictionary(StringComparer.Ordinal);
    }

    public string SaveId { get; }
    public uint Slot { get; }
    public ulong Revision { get; }
    public Game Source { get; }
    public IReadOnlyDictionary<string, uint> Values { get; }
    public IReadOnlyDictionary<string, uint> Applied { get; }

    public string Serialize() => JsonSerializer.Serialize(new
    {
        version = CurrentVersion,
        saveId = SaveId,
        slot = Slot,
        revision = Revision,
        source = GameInfo.ToSeedTag(Source),
        values = Values.OrderBy(p => p.Key, StringComparer.Ordinal).ToDictionary(),
        applied = Applied.OrderBy(p => p.Key, StringComparer.Ordinal).ToDictionary(),
    });

    public static bool TryParse(string text, string expectedSaveId, uint expectedSlot,
        out SharedResourceSnapshot? snapshot, out string error)
    {
        snapshot = null;
        error = "Invalid shared-resource snapshot.";
        if (text.Length > 262_144 || expectedSlot > 2 || string.IsNullOrEmpty(expectedSaveId))
            return false;
        try
        {
            using var document = JsonDocument.Parse(text, new JsonDocumentOptions { MaxDepth = 8 });
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                return false;
            var properties = root.EnumerateObject().ToArray();
            if (properties.Length != 7 || properties.Select(p => p.Name).Distinct(StringComparer.Ordinal).Count() != 7
                || !root.TryGetProperty("version", out var version) || !version.TryGetInt32(out var v)
                || v != CurrentVersion
                || !root.TryGetProperty("saveId", out var saveId) || saveId.ValueKind != JsonValueKind.String
                || !string.Equals(saveId.GetString(), expectedSaveId, StringComparison.Ordinal)
                || !root.TryGetProperty("slot", out var slot) || !slot.TryGetUInt32(out var s) || s != expectedSlot
                || !root.TryGetProperty("revision", out var revision) || !revision.TryGetUInt64(out var r)
                || r == 0 || r > MaxRevision
                || !root.TryGetProperty("source", out var source) || source.ValueKind != JsonValueKind.String
                || source.GetString() is not ("oot" or "mm")
                || !root.TryGetProperty("values", out var values)
                || !ReadMap(values, 32, 65_535, false, out var valueMap)
                || !root.TryGetProperty("applied", out var applied)
                || !ReadMap(applied, 1_024, SessionInventory.MaxValue, true, out var appliedMap))
                return false;
            snapshot = new SharedResourceSnapshot(expectedSaveId, s, r,
                source.GetString() == "oot" ? Game.Oot : Game.Mm, valueMap, appliedMap);
            error = "";
            return true;
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException)
        {
            return false;
        }
    }

    private static bool ReadMap(JsonElement element, int limit, uint maximum, bool rewards,
        out Dictionary<string, uint> result)
    {
        result = new Dictionary<string, uint>(StringComparer.Ordinal);
        if (element.ValueKind != JsonValueKind.Object)
            return false;
        foreach (var property in element.EnumerateObject())
        {
            var name = property.Name;
            if (result.Count >= limit || name.Length is 0 or > 96
                || (rewards && !name.StartsWith("SHARED_", StringComparison.Ordinal))
                || !name.All(c => char.IsAsciiLetterOrDigit(c) || c == '_')
                || property.Value.ValueKind != JsonValueKind.Number
                || !property.Value.TryGetUInt32(out var value) || value > maximum
                || !result.TryAdd(name, value))
                return false;
        }
        return true;
    }

    public static bool TrySelect(IEnumerable<SharedResourceSnapshot> candidates, string saveId, uint slot,
        out SharedResourceSnapshot? latest, out string error)
    {
        latest = null;
        error = "";
        var revisions = new Dictionary<ulong, string>();
        foreach (var candidate in candidates)
        {
            if (candidate.SaveId != saveId || candidate.Slot != slot)
            {
                latest = null;
                error = "Shared-resource snapshot belongs to another save or slot.";
                return false;
            }
            var serialized = candidate.Serialize();
            if (revisions.TryGetValue(candidate.Revision, out var existing) && existing != serialized)
            {
                latest = null;
                error = "Conflicting shared-resource snapshots have the same revision.";
                return false;
            }
            revisions[candidate.Revision] = serialized;
            if (latest is null || candidate.Revision > latest.Revision)
                latest = candidate;
        }
        return true;
    }
}
