namespace OotMM.Core;

public enum ContentClass
{
    Normal,
    BossKey,
    Major,
    Key,
    Spider,
    Fairy,
    Heart,
    Soul,
    MapCompass,
}

/// <summary>What a check holds, by the same classes the games use for container appearance.</summary>
public static partial class ContainerContents
{
    private static readonly Dictionary<string, string> Substitutions = new(StringComparer.Ordinal)
    {
        ["OOT_SWORD"] = "OOT_SWORD_KOKIRI",
        ["OOT_SWORD_GORON"] = "OOT_SWORD_KNIFE",
        ["OOT_OCARINA"] = "OOT_OCARINA_FAIRY",
        ["OOT_STRENGTH"] = "OOT_GORON_BRACELET",
        ["OOT_SCALE"] = "OOT_SCALE_BRONZE",
        ["OOT_SHIELD"] = "OOT_PROGRESSIVE_SHIELD_DEKU",
        ["OOT_SEEDS_5"] = "OOT_DEKU_SEEDS_5",
        ["OOT_SEEDS_30"] = "OOT_DEKU_SEEDS_30",
        ["MM_SWORD"] = "MM_SWORD_KOKIRI",
        ["MM_SHIELD"] = "MM_PROGRESSIVE_SHIELD_DEKU",
        ["MM_OCARINA"] = "MM_OCARINA_OF_TIME",
        ["MM_SCALE"] = "MM_SCALE_BRONZE",
        ["MM_STRENGTH"] = "MM_GORON_BRACELET",
        ["MM_CLOCK"] = "MM_CLOCK1",
        ["MM_SEEDS_5"] = "MM_DEKU_SEEDS_5",
        ["MM_SEEDS_30"] = "MM_DEKU_SEEDS_30",
        ["MM_MASK_GERUDO"] = "OOT_MASK_GERUDO",
        ["MM_MASK_SKULL"] = "OOT_MASK_SKULL",
        ["MM_MASK_SPOOKY"] = "OOT_MASK_SPOOKY",
    };

    private static readonly string[] OotBombchus = ["OOT_BOMBCHU_5", "OOT_BOMBCHU_10", "OOT_BOMBCHU_20"];
    private static readonly string[] MmBombchus = ["MM_BOMBCHU", "MM_BOMBCHU_5", "MM_BOMBCHU_10", "MM_BOMBCHU_20"];

    /// <summary>The class of the item a check shows, honouring cloaks and the seed's appearance settings.</summary>
    public static ContentClass Classify(Seed seed, ItemPlacement placement)
    {
        var visual = placement.Cloak?.Id ?? placement.Item.Id;
        if (visual.Contains("_TRAP_", StringComparison.Ordinal) && seed.GetBoolSetting("cloakTraps"))
        {
            return ContentClass.Major;
        }

        if (IsBagFirstBombchu(seed, visual))
        {
            return ContentClass.Major;
        }

        var kind = Lookup(placement.Check.Game, visual);
        if (kind == ContentClass.Heart && !seed.GetBoolSetting("csmcHearts", true))
        {
            return ContentClass.Normal;
        }

        if (kind == ContentClass.MapCompass && !seed.GetBoolSetting("csmcMapCompass", true))
        {
            return ContentClass.Normal;
        }

        return kind;
    }

    /// <summary>Whether og counts an item as major, by its own id rather than what the check shows.</summary>
    public static bool IsMajorItem(Game game, string itemId) => Lookup(game, itemId) == ContentClass.Major;

    /// <summary>Whether a game shows what its checks hold, given the seed's rule and the items the player owns.</summary>
    public static bool IsRevealed(Seed seed, Game game, Func<string, bool> owns)
    {
        switch (seed.GetStringSetting("csmc", "always"))
        {
            case "never":
                return false;
            case "agony":
                break;
            default:
                return true;
        }

        if (game == Game.Oot)
        {
            return owns("OOT_STONE_OF_AGONY") || owns("SHARED_STONE_OF_AGONY");
        }

        if (seed.GetBoolSetting("sharedStoneAgony"))
        {
            return owns("SHARED_STONE_OF_AGONY");
        }

        return owns(seed.GetBoolSetting("stoneAgonyMm") ? "MM_STONE_OF_AGONY" : "OOT_STONE_OF_AGONY");
    }

    private static ContentClass Lookup(Game game, string itemId)
    {
        var table = Table.Value;
        if (itemId.StartsWith("SHARED_", StringComparison.Ordinal))
        {
            var suffix = itemId[7..];
            var self = game == Game.Oot ? "OOT_" : "MM_";
            var other = game == Game.Oot ? "MM_" : "OOT_";
            return table.TryGetValue(Substitute(self + suffix), out var own) ? own
                : table.GetValueOrDefault(Substitute(other + suffix), ContentClass.Normal);
        }

        return table.GetValueOrDefault(Substitute(itemId), ContentClass.Normal);
    }

    private static string Substitute(string itemId) => Substitutions.GetValueOrDefault(itemId, itemId);

    private static bool IsBagFirstBombchu(Seed seed, string itemId)
    {
        var oot = seed.GetStringSetting("bombchuBehaviorOot", "free") == "bagFirst";
        var mm = seed.GetStringSetting("bombchuBehaviorMm", "free") == "bagFirst";
        if (OotBombchus.Contains(itemId))
        {
            return oot;
        }

        if (MmBombchus.Contains(itemId))
        {
            return mm;
        }

        return itemId.StartsWith("SHARED_BOMBCHU", StringComparison.Ordinal) && (oot || mm);
    }
}
