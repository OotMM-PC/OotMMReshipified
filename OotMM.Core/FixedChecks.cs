namespace OotMM.Core;

public static class FixedChecks
{
    private static readonly HashSet<string> OneTimeShopChecks = new(StringComparer.Ordinal)
    {
        "MM Bomb Shop Bomb Bag", "MM Bomb Shop Bomb Bag 2", "MM Curiosity Shop All-Night Mask",
    };

    private static readonly HashSet<string> MmScrubs = new(StringComparer.Ordinal)
    {
        "MM Southern Swamp Scrub Shop", "MM Zora Hall Scrub Shop", "MM Ikana Valley Scrub Shop",
    };

    private static readonly HashSet<string> OotMerchants = new(StringComparer.Ordinal)
    {
        "OOT Haunted Wasteland Carpet Merchant", "OOT Kakariko Potion Shop Buy Blue Potion",
        "OOT Lon Lon Ranch Talon Buy Milk",
    };

    private static readonly HashSet<string> MmMerchants = new(StringComparer.Ordinal)
    {
        "MM Gorman Track Milk Purchase", "MM Milk Bar Purchase Milk", "MM Milk Bar Purchase Chateau",
    };

    private static readonly HashSet<string> OotOneTimeScrubs = new(StringComparer.Ordinal)
    {
        "OOT Lost Woods Scrub Sticks Upgrade", "OOT Lost Woods Grotto Scrub Nuts Upgrade",
        "OOT Hyrule Field Grotto Scrub HP",
    };

    private static readonly HashSet<string> OotFrogs = new(StringComparer.Ordinal)
    {
        "OOT Zora River Frogs Zeldas Lullaby", "OOT Zora River Frogs Eponas Song",
        "OOT Zora River Frogs Sarias Song", "OOT Zora River Frogs Suns Song",
        "OOT Zora River Frogs Song of Time",
    };

    private static readonly HashSet<string> MmLottery = new(StringComparer.Ordinal)
    {
        "MM Lottery Prize Night 1", "MM Lottery Prize Night 2", "MM Lottery Prize Night 3",
    };

    private static readonly HashSet<string> SmallKeysOot = new(StringComparer.Ordinal)
    {
        "OOT_SMALL_KEY_FOREST", "OOT_SMALL_KEY_FIRE", "OOT_SMALL_KEY_WATER", "OOT_SMALL_KEY_SPIRIT",
        "OOT_SMALL_KEY_SHADOW", "OOT_SMALL_KEY_BOTW", "OOT_SMALL_KEY_GTG", "OOT_SMALL_KEY_GANON",
    };

    private static readonly HashSet<string> SmallKeysMm = new(StringComparer.Ordinal)
    {
        "MM_SMALL_KEY_WF", "MM_SMALL_KEY_SH", "MM_SMALL_KEY_GB", "MM_SMALL_KEY_ST",
    };

    private static readonly HashSet<string> BossKeysOot = new(StringComparer.Ordinal)
    {
        "OOT_BOSS_KEY_FOREST", "OOT_BOSS_KEY_FIRE", "OOT_BOSS_KEY_WATER", "OOT_BOSS_KEY_SPIRIT",
        "OOT_BOSS_KEY_SHADOW",
    };

    private static readonly HashSet<string> BossKeysMm = new(StringComparer.Ordinal)
    {
        "MM_BOSS_KEY_WF", "MM_BOSS_KEY_SH", "MM_BOSS_KEY_GB", "MM_BOSS_KEY_ST",
    };

    private static readonly HashSet<string> DungeonStrayFairies = new(StringComparer.Ordinal)
    {
        "MM_STRAY_FAIRY_WF", "MM_STRAY_FAIRY_SH", "MM_STRAY_FAIRY_GB", "MM_STRAY_FAIRY_ST",
    };

    private static readonly HashSet<string> TingleMaps = new(StringComparer.Ordinal)
    {
        "MM_WORLD_MAP_CLOCK_TOWN", "MM_WORLD_MAP_WOODFALL", "MM_WORLD_MAP_SNOWHEAD",
        "MM_WORLD_MAP_ROMANI_RANCH", "MM_WORLD_MAP_GREAT_BAY", "MM_WORLD_MAP_STONE_TOWER",
    };

    private static readonly HashSet<string> Owls = new(StringComparer.Ordinal)
    {
        "MM_OWL_GREAT_BAY", "MM_OWL_ZORA_CAPE", "MM_OWL_SNOWHEAD", "MM_OWL_MOUNTAIN_VILLAGE",
        "MM_OWL_CLOCK_TOWN", "MM_OWL_MILK_ROAD", "MM_OWL_WOODFALL", "MM_OWL_SOUTHERN_SWAMP",
        "MM_OWL_IKANA_CANYON", "MM_OWL_STONE_TOWER",
    };

    private const uint OotTreasureShopScene = 0x10;

    public static bool IsFixed(Seed seed, ItemPlacement placement)
    {
        var check = placement.Check;
        var item = placement.Item.Id;
        var name = check.Name;
        var game = check.Game;
        var type = check.Type;

        if (TingleMaps.Contains(item) && seed.GetStringSetting("tingleShuffle") == "vanilla")
            return true;
        if (Owls.Contains(item) && seed.GetStringSetting("owlShuffle") == "none")
            return true;
        if (!seed.GetBoolSetting("shuffleMerchantsMm") && MmMerchants.Contains(name))
            return true;
        if (!seed.GetBoolSetting("shuffleMerchantsOot") && OotMerchants.Contains(name))
            return true;
        if (!seed.GetBoolSetting("shuffleFrogsRupeesOot") && OotFrogs.Contains(name))
            return true;
        if (!seed.GetBoolSetting("shuffleLotteryMm") && MmLottery.Contains(name))
            return true;
        if (type == "cow")
        {
            if (game == Game.Oot && !seed.GetBoolSetting("cowShuffleOot"))
                return true;
            if (game == Game.Mm && !seed.GetBoolSetting("cowShuffleMm"))
                return true;
        }
        if (type == "shop")
        {
            if (game == Game.Oot && seed.GetStringSetting("shopShuffleOot") == "none")
                return true;
            if (game == Game.Mm && seed.GetStringSetting("shopShuffleMm") == "none" && !OneTimeShopChecks.Contains(name))
                return true;
        }
        if (type == "scrub" && game == Game.Oot && !seed.GetBoolSetting("scrubShuffleOot")
            && !OotOneTimeScrubs.Contains(name))
            return true;
        if (MmScrubs.Contains(name) && !seed.GetBoolSetting("scrubShuffleMm"))
            return true;
        if (type == "sr" && seed.GetStringSetting("silverRupeeShuffle") == "vanilla")
            return true;
        if (item == "OOT_GERUDO_CARD" && !seed.GetBoolSetting("shuffleGerudoCard"))
            return true;
        if (item == "OOT_SWORD_MASTER" && !seed.GetBoolSetting("shuffleMasterSword") && !MustStartWithMasterSword(seed))
            return true;
        if (item == "OOT_BOSS_KEY_GANON" && seed.GetStringSetting("ganonBossKey") == "vanilla")
            return true;
        if (item == "OOT_SMALL_KEY_GF" && seed.GetStringSetting("smallKeyShuffleHideout") == "vanilla")
            return true;
        if (game == Game.Oot && check.SceneId == OotTreasureShopScene && item != "OOT_HEART_PIECE"
            && seed.GetStringSetting("smallKeyShuffleChestGame") == "vanilla")
            return true;
        if (item == "MM_STRAY_FAIRY_TOWN" && seed.GetStringSetting("townFairyShuffle") == "vanilla")
            return true;
        if (DungeonStrayFairies.Contains(item) && IsStrayFairyFixed(seed, type))
            return true;
        if (item == "OOT_OCARINA" && !seed.GetBoolSetting("shuffleOcarinasOot"))
            return true;
        if (item.StartsWith("OOT_FISHING_POND_", StringComparison.Ordinal) && !seed.GetBoolSetting("pondFishShuffle"))
            return true;
        if (SmallKeysOot.Contains(item) && seed.GetStringSetting("smallKeyShuffleOot") == "vanilla")
            return true;
        if (SmallKeysMm.Contains(item) && seed.GetStringSetting("smallKeyShuffleMm") == "vanilla")
            return true;
        if (BossKeysOot.Contains(item) && seed.GetStringSetting("bossKeyShuffleOot") == "vanilla")
            return true;
        if (BossKeysMm.Contains(item) && seed.GetStringSetting("bossKeyShuffleMm") == "vanilla")
            return true;
        if (seed.GetStringSetting("housesSkulltulaTokens") != "cross")
        {
            if (item == "OOT_GS_TOKEN")
            {
                switch (seed.GetStringSetting("goldSkulltulaTokens"))
                {
                    case "none":
                        return true;
                    case "overworld":
                        return type == "gs" && IsOotDungeonScene(check);
                    case "dungeons":
                        return type == "gs" && game == Game.Oot && !IsOotDungeonScene(check);
                }
            }
            if (item is "MM_GS_TOKEN_SWAMP" or "MM_GS_TOKEN_OCEAN" && seed.GetStringSetting("housesSkulltulaTokens") == "none")
                return true;
        }
        return false;
    }

    private static bool IsStrayFairyFixed(Seed seed, string type)
    {
        var chestVanilla = seed.GetStringSetting("strayFairyChestShuffle") == "vanilla";
        var otherVanilla = seed.GetStringSetting("strayFairyOtherShuffle") == "vanilla";
        if (chestVanilla && otherVanilla)
            return true;
        if (chestVanilla && type == "chest")
            return !StrayFairiesCanLeaveTheirSpots(seed.GetStringSetting("strayFairyOtherShuffle"));
        if (otherVanilla && type != "chest")
            return !StrayFairiesCanLeaveTheirSpots(seed.GetStringSetting("strayFairyChestShuffle"));
        return false;
    }

    private static bool StrayFairiesCanLeaveTheirSpots(string mode) => mode is "anywhere" or "ownDungeon";

    private static bool IsOotDungeonScene(CheckRef check) =>
        check.Game == Game.Oot && check.SceneId is { } scene && (scene <= 0x0D || scene is >= 0x11 and <= 0x19);

    private static bool MustStartWithMasterSword(Seed seed)
    {
        if (seed.GetStringSetting("startingAge") == "child")
            return false;
        if (!seed.GetBoolSetting("swordlessAdult"))
            return true;
        return !seed.GetBoolSetting("shuffleMasterSword") && seed.GetBoolSetting("timeTravelSword");
    }
}
