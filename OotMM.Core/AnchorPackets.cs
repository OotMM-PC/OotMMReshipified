using System.Text.Json;

namespace OotMM.Core;

public sealed record CheckCompletePacket(Game CheckGame, string CheckId, string ItemName);

public static class AnchorPackets
{
    public const string CheckCompleteType = "OOTMM_CHECK_COMPLETE";
    public const string CrossGameTransitionType = "OOTMM_CROSS_GAME_TRANSITION";

    public static string? PacketType(string jsonText)
    {
        try
        {
            using var doc = JsonDocument.Parse(jsonText);
            return doc.RootElement.ValueKind == JsonValueKind.Object
                   && doc.RootElement.TryGetProperty("type", out var type)
                   && type.ValueKind == JsonValueKind.String
                ? type.GetString()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public static CheckCompletePacket? ParseCheckComplete(string jsonText)
    {
        try
        {
            using var doc = JsonDocument.Parse(jsonText);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("type", out var type)
                || !type.ValueEquals(CheckCompleteType))
            {
                return null;
            }

            if (!root.TryGetProperty("checkGame", out var game)
                || game.ValueKind != JsonValueKind.String
                || GameInfo.TryFromSeedTag(game.GetString()!) is not { } checkGame
                || !root.TryGetProperty("checkId", out var checkId)
                || checkId.ValueKind != JsonValueKind.String)
            {
                return null;
            }

            var itemName = root.TryGetProperty("itemName", out var name)
                           && name.ValueKind == JsonValueKind.String
                ? name.GetString()!
                : "";
            return new CheckCompletePacket(checkGame, checkId.GetString()!, itemName);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public static CrossGameTransition? ParseCrossGameTransition(string jsonText)
    {
        try
        {
            using var doc = JsonDocument.Parse(jsonText);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("type", out var type)
                || !type.ValueEquals(CrossGameTransitionType))
            {
                return null;
            }

            if (!root.TryGetProperty("fromGame", out var from)
                || from.ValueKind != JsonValueKind.String
                || GameInfo.TryFromSeedTag(from.GetString()!) is not { } fromGame
                || !root.TryGetProperty("toGame", out var to)
                || to.ValueKind != JsonValueKind.String
                || GameInfo.TryFromSeedTag(to.GetString()!) is not { } toGame
                || !root.TryGetProperty("toEntrance", out var toEntrance)
                || toEntrance.ValueKind != JsonValueKind.String)
            {
                return null;
            }

            return new CrossGameTransition(
                fromGame, toGame, toEntrance.GetString()!,
                ReadU32(root, "toNativeId"), ReadU32(root, "ootAge"));
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public static string BuildCrossGameTransition(CrossGameTransition transition, Seed seed) =>
        JsonSerializer.Serialize(new
        {
            type = CrossGameTransitionType,
            seedId = seed.SeedId,
            settingsHash = seed.SettingsHash,
            teamId = seed.TeamId,
            playerId = seed.PlayerId,
            fromGame = GameInfo.ToSeedTag(transition.FromGame),
            toGame = GameInfo.ToSeedTag(transition.ToGame),
            toEntrance = transition.ToEntrance,
            toNativeId = transition.ToNativeId,
            ootAge = transition.OotAge,
        });

    public static bool MatchesSeedIdentity(string jsonText, Seed seed)
    {
        try
        {
            using var doc = JsonDocument.Parse(jsonText);
            var root = doc.RootElement;
            return ReadString(root, "seedId") == seed.SeedId
                   && ReadString(root, "settingsHash") == seed.SettingsHash
                   && ReadString(root, "teamId") == seed.TeamId
                   && root.TryGetProperty("playerId", out var player)
                   && player.ValueKind == JsonValueKind.Number
                   && player.TryGetUInt16(out var playerId)
                   && playerId == seed.PlayerId;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static string ReadString(JsonElement root, string key) =>
        root.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()!
            : "";

    private static uint? ReadU32(JsonElement root, string key) =>
        root.TryGetProperty(key, out var value)
        && value.ValueKind == JsonValueKind.Number
        && value.TryGetUInt32(out var parsed)
            ? parsed
            : null;
}
