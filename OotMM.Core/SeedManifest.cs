using System.Text;
using System.Text.Json;

namespace OotMM.Core;

/// <summary>A verified OOTMM_SEED_MANIFEST upload: the sender's world as a Seed
/// (identity + placements only) with its protocol epoch and manifest hash.</summary>
public sealed record SeedManifestRegistration(Seed Seed, ulong SessionEpoch, string ManifestHash);

/// <summary>The OOTMM_SEED_MANIFEST packet codec (AnchorBridge.cpp). Build is
/// byte-identical to the C++ writer; Parse re-verifies the manifest hash.</summary>
public static class SeedManifest
{
    public static string Build(Seed seed)
    {
        var text = new StringBuilder();
        text.Append("{\"type\":").Append(JsonText.EscapeString(RelayProtocol.SeedManifest))
            .Append(",\"seedId\":").Append(JsonText.EscapeString(seed.SeedId))
            .Append(",\"settingsHash\":").Append(JsonText.EscapeString(seed.SettingsHash))
            .Append(",\"generatorVersion\":").Append(JsonText.EscapeString(seed.GeneratorVersion))
            .Append(",\"teamId\":").Append(JsonText.EscapeString(seed.TeamId))
            .Append(",\"playerId\":").Append(seed.PlayerId)
            .Append(",\"playerCount\":").Append(seed.PlayerCount)
            .Append(",\"protocolVersion\":").Append(RelayProtocol.AnchorProtocolVersion)
            .Append(",\"sessionEpoch\":").Append(JsonText.EscapeString(seed.SessionEpoch.ToString()))
            .Append(",\"manifestHash\":").Append(JsonText.EscapeString(Identity.SeedManifestHash(seed)))
            .Append(",\"format\":").Append(JsonText.EscapeString(seed.Format));
        if (seed.GetBoolSetting("trapsLink"))
        {
            // Emitted only when on; absence reads as the OFF default.
            text.Append(",\"trapsLink\":true");
        }

        text.Append(",\"placements\":[");
        var first = true;
        foreach (var placement in seed.Placements)
        {
            if (!first)
            {
                text.Append(',');
            }

            first = false;
            text.Append("{\"checkGame\":").Append(JsonText.EscapeString(GameInfo.ToSeedTag(placement.Check.Game)))
                .Append(",\"checkId\":").Append(JsonText.EscapeString(placement.Check.Id))
                .Append(",\"itemGame\":").Append(JsonText.EscapeString(GameInfo.ToSeedTag(placement.Item.Game)))
                .Append(",\"itemId\":").Append(JsonText.EscapeString(placement.Item.Id))
                .Append(",\"itemName\":").Append(JsonText.EscapeString(placement.Item.Name))
                .Append(",\"ownerPlayer\":").Append(placement.OwnerPlayer)
                .Append('}');
        }

        return text.Append("]}").ToString();
    }

    public static SeedManifestRegistration? Parse(string jsonText)
    {
        try
        {
            using var doc = JsonDocument.Parse(jsonText);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !IsAnchorProtocol(root)
                || ReadString(root, "type") != RelayProtocol.SeedManifest)
            {
                return null;
            }

            var playerId = ReadU16(root, "playerId");
            var playerCount = ReadU16(root, "playerCount");
            var sessionEpoch = ReadU64(root, "sessionEpoch");
            var manifestHash = ReadString(root, "manifestHash");
            var boolSettings = new Dictionary<string, bool>();
            if (root.TryGetProperty("trapsLink", out var trapsLink))
            {
                if (trapsLink.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                {
                    return null;
                }

                boolSettings["trapsLink"] = trapsLink.GetBoolean();
            }

            if (!root.TryGetProperty("placements", out var placementsElement)
                || placementsElement.ValueKind != JsonValueKind.Array)
            {
                return null;
            }

            var placements = new List<ItemPlacement>();
            var checks = new HashSet<(Game, string)>();
            foreach (var entry in placementsElement.EnumerateArray())
            {
                if (entry.ValueKind != JsonValueKind.Object
                    || GameInfo.TryFromSeedTag(ReadString(entry, "checkGame")) is not { } checkGame
                    || GameInfo.TryFromSeedTag(ReadString(entry, "itemGame")) is not { } itemGame)
                {
                    return null;
                }

                var checkId = ReadString(entry, "checkId");
                var itemId = ReadString(entry, "itemId");
                var ownerPlayer = ReadU16(entry, "ownerPlayer");
                if (checkId.Length == 0 || itemId.Length == 0 || ownerPlayer > playerCount
                    || !checks.Add((checkGame, checkId)))
                {
                    return null;
                }

                placements.Add(new ItemPlacement(
                    new CheckRef(checkGame, checkId, "", [], null, "", null),
                    new ItemRef(itemGame, itemId, ReadString(entry, "itemName")),
                    Cloak: null,
                    OwnerPlayer: ownerPlayer,
                    SourcePlayer: playerId,
                    Major: false));
            }

            var seed = new Seed
            {
                Format = ReadString(root, "format"),
                GeneratorVersion = ReadString(root, "generatorVersion"),
                SeedId = ReadString(root, "seedId"),
                SettingsHash = ReadString(root, "settingsHash"),
                TeamId = ReadString(root, "teamId"),
                PlayerId = playerId,
                PlayerCount = playerCount,
                SessionEpoch = sessionEpoch,
                Placements = placements,
                BoolSettings = boolSettings,
            };
            if (seed.Format != Seed.CurrentFormat
                || seed.GeneratorVersion != Seed.RequiredGeneratorVersion
                || seed.SeedId.Length == 0 || playerId == 0 || playerCount == 0
                || playerId > playerCount || sessionEpoch == 0
                || manifestHash != Identity.SeedManifestHash(seed))
            {
                return null;
            }

            return new SeedManifestRegistration(seed, sessionEpoch, manifestHash);
        }
        catch (Exception ex) when (ex is JsonException or FormatException or InvalidOperationException)
        {
            return null;
        }
    }

    private static bool IsAnchorProtocol(JsonElement root) =>
        root.TryGetProperty("protocolVersion", out var version)
        && version.ValueKind == JsonValueKind.Number
        && version.TryGetDouble(out var value)
        && double.IsFinite(value) && Math.Truncate(value) == value
        && value == RelayProtocol.AnchorProtocolVersion;

    private static string ReadString(JsonElement element, string key)
    {
        if (!element.TryGetProperty(key, out var value) || value.ValueKind != JsonValueKind.String)
        {
            throw new FormatException($"Invalid Anchor packet string: {key}");
        }

        return value.GetString()!;
    }

    private static ushort ReadU16(JsonElement element, string key)
    {
        if (!element.TryGetProperty(key, out var value) || value.ValueKind != JsonValueKind.Number
            || !value.TryGetDouble(out var number) || !double.IsFinite(number)
            || Math.Truncate(number) != number || number is < 0 or > 65535)
        {
            throw new FormatException($"Invalid Anchor packet number: {key}");
        }

        return (ushort)number;
    }

    private static ulong ReadU64(JsonElement element, string key)
    {
        if (!element.TryGetProperty(key, out var value))
        {
            throw new FormatException($"Invalid Anchor packet number: {key}");
        }

        if (value.ValueKind == JsonValueKind.String)
        {
            var text = value.GetString()!;
            if (text.Length == 0 || text.Any(ch => ch is < '0' or > '9')
                || !ulong.TryParse(text, out var parsed))
            {
                throw new FormatException($"Invalid Anchor packet number: {key}");
            }

            return parsed;
        }

        if (value.ValueKind == JsonValueKind.Number && value.TryGetUInt64(out var number))
        {
            return number;
        }

        throw new FormatException($"Invalid Anchor packet number: {key}");
    }
}
