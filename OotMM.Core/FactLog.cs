using System.Text;
using System.Text.Json;

namespace OotMM.Core;

public sealed record Fact(ushort SourcePlayer, Game Game, string CheckId, ulong Seq = 0);

public static class FactLog
{
    public static string EncodeFact(Fact fact)
    {
        var line = new StringBuilder("{\"v\":4,");
        if (fact.Seq != 0)
        {
            line.Append("\"seq\":").Append(fact.Seq).Append(',');
        }

        AppendNaturalKey(line, fact);
        return line.Append('}').ToString();
    }

    public static Fact? DecodeFact(string line)
    {
        using var doc = ParseV4Frame(line);
        if (doc is null)
        {
            return null;
        }

        var root = doc.RootElement;
        if (root.TryGetProperty("ack", out _)
            || root.TryGetProperty("since", out _)
            || root.TryGetProperty("live", out _))
        {
            return null;
        }

        var fact = ReadNaturalKey(root);
        if (fact is null)
        {
            return null;
        }

        if (root.TryGetProperty("seq", out var seqField))
        {
            var seq = Json64.ReadU64(seqField);
            if (seq is null)
            {
                return null;
            }

            fact = fact with { Seq = seq.Value };
        }

        return fact;
    }

    public static string EncodeAck(Fact fact)
    {
        var line = new StringBuilder("{\"v\":4,\"ack\":{");
        AppendNaturalKey(line, fact);
        return line.Append("},\"seq\":").Append(fact.Seq).Append('}').ToString();
    }

    public static Fact? DecodeAck(string line)
    {
        using var doc = ParseV4Frame(line);
        if (doc is null
            || !doc.RootElement.TryGetProperty("ack", out var ack)
            || ack.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var fact = ReadNaturalKey(ack);
        if (fact is null)
        {
            return null;
        }

        if (doc.RootElement.TryGetProperty("seq", out var seqField))
        {
            var seq = Json64.ReadU64(seqField);
            if (seq is null)
            {
                return null;
            }

            fact = fact with { Seq = seq.Value };
        }

        return fact;
    }

    public static string EncodeCatchUp(ulong since) => $"{{\"v\":4,\"since\":{since}}}";

    public static ulong? DecodeCatchUp(string line)
    {
        using var doc = ParseV4Frame(line);
        return doc is not null && doc.RootElement.TryGetProperty("since", out var since)
            ? Json64.ReadU64(since)
            : null;
    }

    public static string EncodeLive() => "{\"v\":4,\"live\":true}";

    public static bool DecodeLive(string line)
    {
        using var doc = ParseV4Frame(line);
        return doc is not null
               && doc.RootElement.TryGetProperty("live", out var live)
               && live.ValueKind == JsonValueKind.True;
    }

    public static string DeliveryKeyFor(Fact fact, Game destination) =>
        $"v4:p{fact.SourcePlayer}:{GameInfo.ToSeedTag(fact.Game)}:{fact.CheckId}"
        + $":to:{GameInfo.ToSeedTag(destination)}";

    private static void AppendNaturalKey(StringBuilder line, Fact fact) =>
        line.Append("\"p\":").Append(fact.SourcePlayer)
            .Append(",\"g\":").Append(JsonText.EscapeString(GameInfo.ToSeedTag(fact.Game)))
            .Append(",\"c\":").Append(JsonText.EscapeString(fact.CheckId));

    private static JsonDocument? ParseV4Frame(string line)
    {
        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(line);
        }
        catch (JsonException)
        {
            return null;
        }

        if (doc.RootElement.ValueKind == JsonValueKind.Object
            && doc.RootElement.TryGetProperty("v", out var version)
            && Json64.ReadU64(version) == 4)
        {
            return doc;
        }

        doc.Dispose();
        return null;
    }

    private static Fact? ReadNaturalKey(JsonElement obj)
    {
        if (!obj.TryGetProperty("p", out var playerField))
        {
            return null;
        }

        var player = Json64.ReadU64(playerField);
        if (player is null or 0 or > ushort.MaxValue)
        {
            return null;
        }

        if (!obj.TryGetProperty("g", out var gameField)
            || gameField.ValueKind != JsonValueKind.String
            || GameInfo.TryFromSeedTag(gameField.GetString()!) is not { } game)
        {
            return null;
        }

        if (!obj.TryGetProperty("c", out var checkField)
            || checkField.ValueKind != JsonValueKind.String
            || checkField.GetString() is not { Length: > 0 } checkId)
        {
            return null;
        }

        return new Fact((ushort)player.Value, game, checkId);
    }
}
