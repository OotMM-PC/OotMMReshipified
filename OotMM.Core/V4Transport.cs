using System.Text;

namespace OotMM.Core;

/// <summary>One own-world journal entry as snapshotted by the session tick.</summary>
public sealed record V4JournalCheck(Game Game, string CheckId, bool Collected);

/// <summary>Ferry ordering: the session log is append-only in relay seq order,
/// no gaps, no duplicates.</summary>
public enum V4FerryAction
{
    Append,
    Duplicate,
    Gap,
}

/// <summary>Resume scan over an existing ferry log: catch-up cursor plus every
/// own fact already logged (a fact in the log IS an ack).</summary>
public sealed record V4FerryScan(ulong HighestSeq, IReadOnlySet<string> OwnKeys);

/// <summary>Pure pieces of the v4 launcher transport (V4Transport.cpp): fact
/// selection, ferry classification, resume scan, sidecar serialization.</summary>
public static class V4Transport
{
    public static string NaturalKey(ushort player, Game game, string checkId) =>
        $"{player}|{GameInfo.ToSeedTag(game)}|{checkId}";

    /// <summary>Unacknowledged collected checks, retried per key and bounded per tick.</summary>
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

        // Only newline-terminated lines count; a torn tail was never committed.
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

    /// <summary>The game-side ForeignPlacementTable feed: every other-world
    /// placement the v3 route derivation addresses to the local player.
    /// Coverage is load-bearing — an empty-but-present list means "no manifest
    /// arrived yet", so this is written at session start regardless.</summary>
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
                if (placement.OwnerPlayer != localSeed.PlayerId
                    && placement.OwnerPlayer != 0 && !linkedTrap)
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
