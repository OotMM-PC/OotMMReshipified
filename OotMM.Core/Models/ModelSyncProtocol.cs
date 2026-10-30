using System.Security.Cryptography;
using System.Text;
using OotMM.Core.Relay;

namespace OotMM.Core.Models;

public sealed record ModelAnnounce(
    int PlayerId, Game Game, string Name, string Hash, long Size, int Index = 0, int Count = 1);

public sealed record ModelRequest(int PlayerId, int TargetPlayer, string Hash);

public sealed record ModelChunk(int PlayerId, string Hash, long Seq, long Total, byte[] Data);

public static class ModelSyncProtocol
{
    public const int ChunkBytes = 48 * 1024;

    public const int MaxSetArchives = 32;

    public static string HashBytes(byte[] data) => Convert.ToHexStringLower(SHA256.HashData(data));

    private static bool IsHexHash(string hash)
    {
        if (hash.Length == 0 || hash.Length > 64)
        {
            return false;
        }

        foreach (var c in hash)
        {
            if (c is not ((>= '0' and <= '9') or (>= 'a' and <= 'f')))
            {
                return false;
            }
        }

        return true;
    }

    private static StringBuilder OpenLine(string type, Seed seed, int sourcePlayer) =>
        new StringBuilder(256)
            .Append("{\"type\":\"").Append(type)
            .Append("\",\"seedId\":").Append(JsonText.EscapeString(seed.SeedId))
            .Append(",\"teamId\":").Append(JsonText.EscapeString(seed.TeamId))
            .Append(",\"sourcePlayer\":").Append(sourcePlayer);

    public static string BuildAnnounce(Seed seed, ModelAnnounce announce) =>
        OpenLine(PresencePackets.ModAnnounce, seed, announce.PlayerId)
            .Append(",\"game\":\"").Append(GameInfo.ToSeedTag(announce.Game))
            .Append("\",\"name\":").Append(JsonText.EscapeString(announce.Name))
            .Append(",\"hash\":").Append(JsonText.EscapeString(announce.Hash))
            .Append(",\"size\":").Append(announce.Size)
            .Append(",\"index\":").Append(announce.Index)
            .Append(",\"count\":").Append(announce.Count)
            .Append('}').ToString();

    public static string BuildRequest(Seed seed, ModelRequest request) =>
        OpenLine(PresencePackets.ModRequest, seed, request.PlayerId)
            .Append(",\"targetPlayer\":").Append(request.TargetPlayer)
            .Append(",\"hash\":").Append(JsonText.EscapeString(request.Hash))
            .Append('}').ToString();

    public static string BuildChunk(Seed seed, ModelChunk chunk) =>
        OpenLine(PresencePackets.ModChunk, seed, chunk.PlayerId)
            .Append(",\"hash\":").Append(JsonText.EscapeString(chunk.Hash))
            .Append(",\"seq\":").Append(chunk.Seq)
            .Append(",\"total\":").Append(chunk.Total)
            .Append(",\"data\":\"").Append(Convert.ToBase64String(chunk.Data))
            .Append("\"}").ToString();

    public static ModelAnnounce? ParseAnnounce(string line)
    {
        var game = GameInfo.TryFromSeedTag(RelayProtocol.ExtractStringField(line, "game"));
        var hash = RelayProtocol.ExtractStringField(line, "hash");
        var size = (long)RelayProtocol.ExtractU64Field(line, "size");
        var player = RelayProtocol.ExtractIntField(line, "sourcePlayer");
        var index = RelayProtocol.ExtractIntField(line, "index", -1);
        var count = RelayProtocol.ExtractIntField(line, "count", -1);
        var name = RelayProtocol.ExtractStringField(line, "name");
        if (game is null || !IsHexHash(hash) || size <= 0 || player <= 0 || count < 0
            || (count == 0 ? index != 0 : index < 0 || index >= count)
            || count > MaxSetArchives || name.Length > 128)
        {
            return null;
        }

        return new ModelAnnounce(player, game.Value, name, hash, size, index, count);
    }

    public static ModelRequest? ParseRequest(string line)
    {
        var hash = RelayProtocol.ExtractStringField(line, "hash");
        var player = RelayProtocol.ExtractIntField(line, "sourcePlayer");
        var target = RelayProtocol.ExtractIntField(line, "targetPlayer");
        return !IsHexHash(hash) || player <= 0 || target <= 0
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
        if (!IsHexHash(hash) || player <= 0 || total <= 0 || seq < 0 || seq >= total || data.Length == 0)
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
