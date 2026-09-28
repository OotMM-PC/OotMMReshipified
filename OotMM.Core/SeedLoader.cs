using System.Text.Json;

namespace OotMM.Core;

public sealed class SeedLoadException(string message) : Exception(message);

/// <summary>Parser for .ootmm.json seeds; field names, defaults, gates and error
/// messages mirror the C++ loader (Seed.cpp). Hints subtree skipped until used.</summary>
public static class SeedLoader
{
    public static Seed LoadFromFile(string path)
    {
        string text;
        try
        {
            text = File.ReadAllText(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new SeedLoadException($"Cannot read seed file: {ex.Message}");
        }

        return LoadFromJson(text);
    }

    public static Seed LoadFromJson(string json)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json);
        }
        catch (JsonException ex)
        {
            throw new SeedLoadException($"Seed is not valid JSON: {ex.Message}");
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                throw new SeedLoadException("Seed root must be a JSON object");
            }

            var format = RequiredString(root, "format");
            if (format != Seed.CurrentFormat)
            {
                throw new SeedLoadException(format == "ootmm.pc.seed.v1"
                    ? "This seed predates v31 PC support; regenerate it"
                    : $"Seed is not compatible with {Seed.CurrentFormat}");
            }

            var generatorVersion = RequiredString(root, "generatorVersion");
            if (generatorVersion != Seed.RequiredGeneratorVersion)
            {
                throw new SeedLoadException("This build requires an OoTMM v31.1 PC seed; regenerate it");
            }

            var playerId = OptionalU16(root, "playerId", 1);
            var sessionEpoch = OptionalU64(root, "sessionEpoch", 1);
            if (sessionEpoch == 0)
            {
                throw new SeedLoadException("Seed sessionEpoch must be nonzero");
            }

            var (boolSettings, stringSettings, numberSettings, specialConds) = ParseSettings(root);

            return new Seed
            {
                Format = format,
                GeneratorVersion = generatorVersion,
                SeedId = RequiredString(root, "seedId"),
                SettingsHash = OptionalString(root, "settingsHash", ""),
                PlayerId = playerId,
                PlayerCount = OptionalU16(root, "playerCount", 1),
                TeamId = OptionalString(root, "teamId", "default"),
                SessionEpoch = sessionEpoch,
                StartingGame = OptionalGame(root, "startingGame"),
                Cosmetics = ParseCosmetics(root),
                SongEventsOot = ParseSongEvents(root, "songEventsOot", 18),
                SongEventsMm = ParseSongEvents(root, "songEventsMm", 13),
                Prices = ParseIntArray(root, "prices"),
                EntrancesSong = ParseWarpTable(root, "entrancesSong", 6),
                EntrancesOwl = ParseWarpTable(root, "entrancesOwl", 10),
                EntrancesSpawn = ParseWarpTable(root, "entrancesSpawn", 2),
                DungeonWarps = ParseWarpTable(root, "dungeonWarps", 12),
                BossIds = ParseBossIds(root),
                BoolSettings = boolSettings,
                StringSettings = stringSettings,
                NumberSettings = numberSettings,
                SpecialConds = specialConds,
                WorldFlags = ParseWorldFlags(root),
                PreCompletedDungeons = ParseStringArray(root, "preCompletedDungeons"),
                Placements = ParsePlacements(root, playerId),
                Entrances = ParseEntrances(root),
                StartingItems = ParseStartingItems(root),
            };
        }
    }

    // --- Section parsers ------------------------------------------------------

    private static Seed.CosmeticsInfo ParseCosmetics(JsonElement root)
    {
        if (!root.TryGetProperty("cosmetics", out var cosmetics))
        {
            return new Seed.CosmeticsInfo(false, true, true, "");
        }

        if (cosmetics.ValueKind != JsonValueKind.Object)
        {
            throw new SeedLoadException("Seed cosmetics must be an object");
        }

        var companionFile = "";
        if (cosmetics.TryGetProperty("musicCompanion", out var companion))
        {
            if (companion.ValueKind != JsonValueKind.Object)
            {
                throw new SeedLoadException("Seed cosmetics.musicCompanion must be an object");
            }

            companionFile = OptionalString(companion, "fileName", "");
        }

        return new Seed.CosmeticsInfo(
            OptionalStrictBool(cosmetics, "gerudoTunic", false, "Seed cosmetics.gerudoTunic must be a boolean"),
            OptionalStrictBool(cosmetics, "musicCategories", true, "Seed cosmetics.musicCategories must be a boolean"),
            OptionalStrictBool(cosmetics, "musicNames", true, "Seed cosmetics.musicNames must be a boolean"),
            companionFile);
    }

    private static List<int> ParseSongEvents(JsonElement root, string key, int requiredCount)
    {
        if (!root.TryGetProperty(key, out var events)
            || events.ValueKind != JsonValueKind.Array
            || events.GetArrayLength() != requiredCount)
        {
            throw new SeedLoadException($"Seed {key} must contain exactly {requiredCount} entries");
        }

        var result = new List<int>(requiredCount);
        foreach (var entry in events.EnumerateArray())
        {
            if (entry.ValueKind != JsonValueKind.Number
                || !entry.TryGetInt32(out var songId)
                || songId is < 0 or > 19)
            {
                throw new SeedLoadException($"Seed {key} entries must be song ids from 0 to 19");
            }

            result.Add(songId);
        }

        return result;
    }

    private static List<uint> ParseWarpTable(JsonElement root, string key, int? count = null)
    {
        var result = new List<uint>();
        if (!root.TryGetProperty(key, out var table))
        {
            return result;
        }

        if (table.ValueKind != JsonValueKind.Array)
        {
            throw new SeedLoadException($"Seed {key} must be an array");
        }

        if (count is { } requiredCount && table.GetArrayLength() != requiredCount)
        {
            throw new SeedLoadException($"Seed {key} must contain exactly {requiredCount} entries");
        }

        foreach (var entry in table.EnumerateArray())
        {
            if (entry.ValueKind != JsonValueKind.Number || !entry.TryGetUInt32(out var value))
            {
                throw new SeedLoadException($"Seed {key} entries must be unsigned 32-bit integers");
            }

            result.Add(value);
        }

        return result;
    }

    private static List<int> ParseBossIds(JsonElement root)
    {
        var result = ParseIntArray(root, "bossIds");
        if (root.TryGetProperty("bossIds", out _)
            && (result.Count != 12 || result.Any(id => id is < 0 or > 11) || result.Distinct().Count() != 12))
        {
            throw new SeedLoadException("Seed bossIds must assign each dungeon from 0 to 11 exactly once");
        }
        return result;
    }

    private static List<int> ParseIntArray(JsonElement root, string key)
    {
        var result = new List<int>();
        if (!root.TryGetProperty(key, out var array))
        {
            return result;
        }

        if (array.ValueKind != JsonValueKind.Array)
        {
            throw new SeedLoadException($"Seed {key} must be an array");
        }

        foreach (var entry in array.EnumerateArray())
        {
            if (entry.ValueKind != JsonValueKind.Number || !entry.TryGetInt32(out var value))
            {
                throw new SeedLoadException($"Seed {key} entries must be numbers");
            }

            result.Add(value);
        }

        return result;
    }

    private static (Dictionary<string, bool>, Dictionary<string, string>, Dictionary<string, double>,
        Dictionary<string, Seed.SpecialCond>) ParseSettings(JsonElement root)
    {
        var bools = new Dictionary<string, bool>();
        var strings = new Dictionary<string, string>();
        var numbers = new Dictionary<string, double>();
        var conds = new Dictionary<string, Seed.SpecialCond>();

        if (!root.TryGetProperty("settings", out var settings)
            || settings.ValueKind != JsonValueKind.Object)
        {
            return (bools, strings, numbers, conds);
        }

        foreach (var property in settings.EnumerateObject())
        {
            switch (property.Value.ValueKind)
            {
                case JsonValueKind.True:
                case JsonValueKind.False:
                    bools[property.Name] = property.Value.GetBoolean();
                    break;
                case JsonValueKind.String:
                    strings[property.Name] = property.Value.GetString()!;
                    break;
                case JsonValueKind.Number:
                    numbers[property.Name] = property.Value.GetDouble();
                    break;
            }
        }

        if (settings.TryGetProperty("specialConds", out var specialConds)
            && specialConds.ValueKind == JsonValueKind.Object)
        {
            foreach (var cond in specialConds.EnumerateObject())
            {
                if (cond.Value.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                var count = 0;
                if (cond.Value.TryGetProperty("count", out var countValue)
                    && countValue.ValueKind == JsonValueKind.Number)
                {
                    count = (int)countValue.GetDouble();
                }

                var fields = new Dictionary<string, bool>();
                foreach (var field in cond.Value.EnumerateObject())
                {
                    if (field.Value.ValueKind is JsonValueKind.True or JsonValueKind.False)
                    {
                        fields[field.Name] = field.Value.GetBoolean();
                    }
                }

                conds[cond.Name] = new Seed.SpecialCond(count, fields);
            }
        }

        return (bools, strings, numbers, conds);
    }

    private static Dictionary<string, Seed.WorldFlag> ParseWorldFlags(JsonElement root)
    {
        var flags = new Dictionary<string, Seed.WorldFlag>();
        if (!root.TryGetProperty("worldFlags", out var worldFlags)
            || worldFlags.ValueKind != JsonValueKind.Object)
        {
            return flags;
        }

        foreach (var property in worldFlags.EnumerateObject())
        {
            // "all" means every member; "none" (or any other scalar) means empty.
            if (property.Value.ValueKind == JsonValueKind.String)
            {
                flags[property.Name] = new Seed.WorldFlag(property.Value.ValueEquals("all"), []);
            }
            else if (property.Value.ValueKind == JsonValueKind.Array)
            {
                var members = property.Value.EnumerateArray()
                    .Where(m => m.ValueKind == JsonValueKind.String)
                    .Select(m => m.GetString()!)
                    .ToList();
                flags[property.Name] = new Seed.WorldFlag(false, members);
            }
            else
            {
                flags[property.Name] = new Seed.WorldFlag(false, []);
            }
        }

        return flags;
    }

    private static List<ItemPlacement> ParsePlacements(JsonElement root, ushort seedPlayerId)
    {
        var placements = new List<ItemPlacement>();
        if (!root.TryGetProperty("placements", out var array))
        {
            return placements;
        }

        if (array.ValueKind != JsonValueKind.Array)
        {
            throw new SeedLoadException("Seed placements must be an array");
        }

        foreach (var entry in array.EnumerateArray())
        {
            if (entry.ValueKind != JsonValueKind.Object)
            {
                throw new SeedLoadException("Seed placement entries must be objects");
            }

            var check = new CheckRef(
                RequiredGame(entry, "checkGame"),
                RequiredString(entry, "checkId"),
                OptionalString(entry, "checkName", ""),
                ParseStringArray(entry, "checkAliases"),
                OptionalU32(entry, "checkScene"),
                OptionalString(entry, "checkType", ""),
                OptionalU32(entry, "checkFlag"));

            var cloak = entry.TryGetProperty("cloakItemId", out _)
                ? ParseItemRef(entry, "cloakItemGame", "cloakItemId", "cloakItemName")
                : null;

            placements.Add(new ItemPlacement(
                check,
                ParseItemRef(entry, "itemGame", "itemId", "itemName"),
                cloak,
                OptionalU16(entry, "ownerPlayer", seedPlayerId),
                OptionalU16(entry, "sourcePlayer", seedPlayerId),
                OptionalBool(entry, "major", false)));
        }

        return placements;
    }

    private static List<EntranceMapping> ParseEntrances(JsonElement root)
    {
        var entrances = new List<EntranceMapping>();
        if (!root.TryGetProperty("entrances", out var array))
        {
            return entrances;
        }

        if (array.ValueKind != JsonValueKind.Array)
        {
            throw new SeedLoadException("Seed entrances must be an array");
        }

        foreach (var entry in array.EnumerateArray())
        {
            if (entry.ValueKind != JsonValueKind.Object)
            {
                throw new SeedLoadException("Seed entrance entries must be objects");
            }

            entrances.Add(new EntranceMapping(
                RequiredGame(entry, "fromGame"),
                RequiredString(entry, "from"),
                OptionalU32(entry, "fromNativeId"),
                RequiredGame(entry, "toGame"),
                RequiredString(entry, "to"),
                OptionalU32(entry, "toNativeId")));
        }

        return entrances;
    }

    private static List<StartingItem> ParseStartingItems(JsonElement root)
    {
        var items = new List<StartingItem>();
        if (!root.TryGetProperty("startingItems", out var array))
        {
            return items;
        }

        if (array.ValueKind != JsonValueKind.Array)
        {
            throw new SeedLoadException("Seed startingItems must be an array");
        }

        foreach (var entry in array.EnumerateArray())
        {
            if (entry.ValueKind != JsonValueKind.Object)
            {
                throw new SeedLoadException("Seed starting item entries must be objects");
            }

            items.Add(new StartingItem(
                ParseItemRef(entry, "itemGame", "itemId", "itemName"),
                OptionalU16(entry, "count", 1)));
        }

        return items;
    }

    // --- Field helpers ---------------------------------------------------------

    private static ItemRef ParseItemRef(JsonElement entry, string gameKey, string idKey, string nameKey) =>
        new(RequiredGame(entry, gameKey), RequiredString(entry, idKey), OptionalString(entry, nameKey, ""));

    private static string RequiredString(JsonElement element, string key)
    {
        if (!element.TryGetProperty(key, out var value)
            || value.ValueKind != JsonValueKind.String
            || value.GetString() is not { Length: > 0 } text)
        {
            throw new SeedLoadException($"Seed {key} must be a non-empty string");
        }

        return text;
    }

    private static string OptionalString(JsonElement element, string key, string fallback) =>
        element.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()!
            : fallback;

    private static bool OptionalBool(JsonElement element, string key, bool fallback) =>
        element.TryGetProperty(key, out var value)
        && value.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? value.GetBoolean()
            : fallback;

    private static bool OptionalStrictBool(JsonElement element, string key, bool fallback, string error)
    {
        if (!element.TryGetProperty(key, out var value))
        {
            return fallback;
        }

        if (value.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
        {
            throw new SeedLoadException(error);
        }

        return value.GetBoolean();
    }

    private static ushort OptionalU16(JsonElement element, string key, ushort fallback)
    {
        if (!element.TryGetProperty(key, out var value))
        {
            return fallback;
        }

        if (value.ValueKind != JsonValueKind.Number
            || !value.TryGetUInt16(out var parsed))
        {
            throw new SeedLoadException($"Seed {key} must be an integer from 0 to 65535");
        }

        return parsed;
    }

    private static uint? OptionalU32(JsonElement element, string key)
    {
        if (!element.TryGetProperty(key, out var value))
        {
            return null;
        }

        if (value.ValueKind != JsonValueKind.Number || !value.TryGetUInt32(out var parsed))
        {
            throw new SeedLoadException($"Seed {key} must be an unsigned 32-bit integer");
        }

        return parsed;
    }

    private static ulong OptionalU64(JsonElement element, string key, ulong fallback)
    {
        if (!element.TryGetProperty(key, out var value))
        {
            return fallback;
        }

        return Json64.ReadU64(value)
               ?? throw new SeedLoadException($"Seed {key} must be an unsigned 64-bit integer");
    }

    private static Game RequiredGame(JsonElement element, string key) =>
        ParseGame(RequiredString(element, key), key);

    private static Game? OptionalGame(JsonElement element, string key)
    {
        if (!element.TryGetProperty(key, out var value) || value.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        return ParseGame(value.GetString()!, key);
    }

    private static Game ParseGame(string tag, string key) =>
        GameInfo.TryFromSeedTag(tag)
        ?? throw new SeedLoadException($"Seed {key} must be \"oot\" or \"mm\"");

    private static List<string> ParseStringArray(JsonElement element, string key)
    {
        var result = new List<string>();
        if (element.TryGetProperty(key, out var array) && array.ValueKind == JsonValueKind.Array)
        {
            foreach (var entry in array.EnumerateArray())
            {
                if (entry.ValueKind == JsonValueKind.String)
                {
                    result.Add(entry.GetString()!);
                }
            }
        }

        return result;
    }
}
