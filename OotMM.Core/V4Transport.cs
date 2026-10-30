using System.Text;

namespace OotMM.Core;

public sealed record V4JournalCheck(Game Game, string CheckId, bool Collected);

public enum V4FerryAction
{
    Append,
    Duplicate,
    Gap,
}

public sealed record V4FerryScan(ulong HighestSeq, IReadOnlySet<string> OwnKeys);

public static class V4Transport
{
    public static string NaturalKey(ushort player, Game game, string checkId) =>
        $"{player}|{GameInfo.ToSeedTag(game)}|{checkId}";

    public static List<Fact> SelectFactsToPublish(
        IReadOnlyList<V4JournalCheck> journal,
        ushort localPlayer,
        IReadOnlySet<string> acked,
        IDictionary<string, TimeSpan> attempts,
        TimeSpan now,
        TimeSpan retryInterval,
        int maxBatch)
    {
        var selected = new List<Fact>();
        foreach (var check in journal)
        {
            if (selected.Count >= maxBatch)
            {
                break;
            }

            if (!check.Collected)
            {
                continue;
            }

            var key = NaturalKey(localPlayer, check.Game, check.CheckId);
            if (acked.Contains(key))
            {
                continue;
            }

            if (attempts.TryGetValue(key, out var lastAttempt) && now - lastAttempt < retryInterval)
            {
                continue;
            }

            attempts[key] = now;
            selected.Add(new Fact(localPlayer, check.Game, check.CheckId, Seq: 0));
        }

        return selected;
    }

    public static V4FerryAction ClassifyFerryLine(ulong seq, ulong highestFerriedSeq)
    {
        if (seq <= highestFerriedSeq)
        {
            return V4FerryAction.Duplicate;
        }

        return seq == highestFerriedSeq + 1 ? V4FerryAction.Append : V4FerryAction.Gap;
    }

    public static V4FerryScan ScanFerryLog(string logPath, ushort localPlayer)
    {
        var ownKeys = new HashSet<string>();
        var highestSeq = 0ul;

        var read = FilePacketBridge.ReadPacketLines(logPath, 0);
        foreach (var line in read.Packets)
        {
            var fact = FactLog.DecodeFact(line);
            if (fact is null || fact.Seq == 0)
            {
                continue;
            }

            highestSeq = Math.Max(highestSeq, fact.Seq);
            if (fact.SourcePlayer == localPlayer)
            {
                ownKeys.Add(NaturalKey(fact.SourcePlayer, fact.Game, fact.CheckId));
            }
        }

        return new V4FerryScan(highestSeq, ownKeys);
    }

    public static string BuildForeignPlacementsSidecar(
        Seed localSeed, IReadOnlyDictionary<ushort, Seed> otherWorlds)
    {
        var trapsLink = localSeed.GetBoolSetting("trapsLink");
        var foreign = otherWorlds
            .Where(world => world.Key != localSeed.PlayerId)
            .OrderBy(world => world.Key)
            .ToList();

        var outText = new StringBuilder();
        outText.Append("{\"v\":4,\"localPlayer\":").Append(localSeed.PlayerId)
               .Append(",\"trapsLink\":").Append(trapsLink ? "true" : "false")
               .Append(",\"coveredWorlds\":[")
               .AppendJoin(',', foreign.Select(world => world.Key))
               .Append("],\"placements\":[");

        var first = true;
        foreach (var (worldPlayer, world) in foreign)
        {
            foreach (var placement in world.Placements)
            {
                var linkedTrap = trapsLink && ItemRules.IsTrapItemId(placement.Item.Id);
                if (!placement.IsFor(localSeed.PlayerId) && !linkedTrap)
                {
                    continue;
                }

                if (!first)
                {
                    outText.Append(',');
                }

                first = false;
                outText.Append("{\"p\":").Append(worldPlayer)
                       .Append(",\"g\":").Append(JsonText.EscapeString(GameInfo.ToSeedTag(placement.Check.Game)))
                       .Append(",\"c\":").Append(JsonText.EscapeString(placement.Check.Id))
                       .Append(",\"itemGame\":").Append(JsonText.EscapeString(GameInfo.ToSeedTag(placement.Item.Game)))
                       .Append(",\"itemId\":").Append(JsonText.EscapeString(placement.Item.Id))
                       .Append(",\"itemName\":").Append(JsonText.EscapeString(placement.Item.Name))
                       .Append('}');
            }
        }

        return outText.Append("]}").ToString();
    }
}
