using System.Security.Cryptography;
using System.Text.Json;
using OotMM.Core.Relay;

namespace OotMM.Core.Models;

public sealed record ModelAnnounce(int PlayerId, Game Game, string Name, string Hash, long Size);

public sealed record ModelRequest(int PlayerId, int TargetPlayer, string Hash);

public sealed record ModelChunk(int PlayerId, string Hash, long Seq, long Total, byte[] Data);

/// <summary>
/// Wire format for shipping a player's model archives around the session. Transfers ride the
/// relay's ephemeral side channel, so every line carries the room identity its allowlist demands.
/// </summary>
public static class ModelSyncProtocol
{
    /// <summary>Raw bytes per chunk. Base64 inflates this by a third, and the result still has to
    /// fit a relay frame alongside its envelope.</summary>
    public const int ChunkBytes = 48 * 1024;

    public static string HashBytes(byte[] data) => Convert.ToHexStringLower(SHA256.HashData(data));

    public static string BuildAnnounce(Seed seed, ModelAnnounce announce) =>
        JsonSerializer.Serialize(new
        {
            type = PresencePackets.ModAnnounce,
            seedId = seed.SeedId,
            teamId = seed.TeamId,
            sourcePlayer = announce.PlayerId,
            game = GameInfo.ToSeedTag(announce.Game),
            name = announce.Name,
            hash = announce.Hash,
            size = announce.Size,
        });

    public static string BuildRequest(Seed seed, ModelRequest request) =>
        JsonSerializer.Serialize(new
        {
            type = PresencePackets.ModRequest,
            seedId = seed.SeedId,
            teamId = seed.TeamId,
            sourcePlayer = request.PlayerId,
            targetPlayer = request.TargetPlayer,
            hash = request.Hash,
        });

    public static string BuildChunk(Seed seed, ModelChunk chunk) =>
        JsonSerializer.Serialize(new
        {
            type = PresencePackets.ModChunk,
            seedId = seed.SeedId,
            teamId = seed.TeamId,
            sourcePlayer = chunk.PlayerId,
            hash = chunk.Hash,
            seq = chunk.Seq,
            total = chunk.Total,
            data = Convert.ToBase64String(chunk.Data),
        });

    public static ModelAnnounce? ParseAnnounce(string line)
    {
        var game = GameInfo.TryFromSeedTag(RelayProtocol.ExtractStringField(line, "game"));
        var hash = RelayProtocol.ExtractStringField(line, "hash");
        var size = (long)RelayProtocol.ExtractU64Field(line, "size");
        var player = RelayProtocol.ExtractIntField(line, "sourcePlayer");
        if (game is null || hash.Length == 0 || size <= 0 || player <= 0)
        {
            return null;
        }

        return new ModelAnnounce(
            player, game.Value, RelayProtocol.ExtractStringField(line, "name"), hash, size);
    }

    public static ModelRequest? ParseRequest(string line)
    {
        var hash = RelayProtocol.ExtractStringField(line, "hash");
        var player = RelayProtocol.ExtractIntField(line, "sourcePlayer");
        var target = RelayProtocol.ExtractIntField(line, "targetPlayer");
        return hash.Length == 0 || player <= 0 || target <= 0
            ? null
            : new ModelRequest(player, target, hash);
    }

    public static ModelChunk? ParseChunk(string line)
    {
        var hash = RelayProtocol.ExtractStringField(line, "hash");
        var player = RelayProtocol.ExtractIntField(line, "sourcePlayer");
        var total = (long)RelayProtocol.ExtractU64Field(line, "total");
        var seq = (long)RelayProtocol.ExtractU64Field(line, "seq");
        var data = RelayProtocol.ExtractStringField(line, "data");
        if (hash.Length == 0 || player <= 0 || total <= 0 || seq < 0 || seq >= total || data.Length == 0)
        {
            return null;
        }

        try
        {
            return new ModelChunk(player, hash, seq, total, Convert.FromBase64String(data));
        }
        catch (FormatException)
        {
            return null;
        }
    }
}
