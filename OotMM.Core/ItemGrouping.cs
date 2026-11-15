namespace OotMM.Core;

public enum ItemGroup
{
    Quest,
    Equipment,
    Items,
    Masks,
    Bottles,
    Trade,
    Buttons,
    Travel,
    Tokens,
    Other,
}

public static class ItemGrouping
{
    private static readonly string[] HiddenPrefixes =
    [
        "TRAP_", "SOUL_", "SONG_", "RUSTY_KEY_", "SMALL_KEY_", "KEY_RING_", "BOSS_KEY_", "MAP_", "COMPASS_",
        "RUPEE_", "POUCH_SILVER_", "FISHING_POND_", "RECOVERY_", "MAGIC_JAR_", "POTION_", "ARROWS_", "BOMBS_",
        "BOMBCHU_", "NUTS_", "STICKS_", "DEKU_SEEDS_", "STRAY_FAIRY_",
    ];

    private static readonly HashSet<string> Hidden = new(StringComparer.Ordinal)
    {
        "BOMB", "BOMBCHU", "NUT", "STICK", "MILK", "CHATEAU", "FAIRY", "FAIRY_BIG", "FISH", "BUGS", "BLUE_FIRE",
        "POE", "BIG_POE", "NOTHING", "SHIELD_DEKU", "SHIELD_HYLIAN", "SHIELD_HERO",
    };

    private static readonly HashSet<string> Equipment = new(StringComparer.Ordinal)
    {
        "STRENGTH", "SCALE", "WALLET", "MAGIC_UPGRADE", "SPIN_UPGRADE", "DEFENSE_UPGRADE", "STICK_UPGRADE",
        "NUT_UPGRADE", "GREAT_FAIRY_SWORD", "BOMB_BAG", "BOMBCHU_BAG", "STONE_OF_AGONY", "GERUDO_CARD",
        "SKELETON_KEY", "RUPEE_MAGICAL", "HEART_PIECE", "HEART_CONTAINER", "SHIELD", "SHIELD_MIRROR",
    };

    private static readonly HashSet<string> Usable = new(StringComparer.Ordinal)
    {
        "BOW", "SLINGSHOT", "HOOKSHOT", "BOOMERANG", "HAMMER", "LENS", "OCARINA", "POWDER_KEG", "PICTOGRAPH_BOX",
        "MAGIC_BEAN", "BOMBER_NOTEBOOK",
    };

    private static readonly HashSet<string> Trade = new(StringComparer.Ordinal)
    {
        "WEIRD_EGG", "CHICKEN", "ZELDA_LETTER", "POCKET_EGG", "POCKET_CUCCO", "COJIRO", "ODD_MUSHROOM",
        "ODD_POTION", "POACHER_SAW", "BROKEN_GORON_SWORD", "PRESCRIPTION", "EYEBALL_FROG", "EYE_DROPS",
        "CLAIM_CHECK", "MOON_TEAR", "ROOM_KEY", "LETTER_TO_KAFEI", "LETTER_TO_MAMA", "PENDANT_OF_MEMORIES",
    };

    /// <summary>The Progress group an item belongs to, or null for items listed elsewhere or not worth listing.</summary>
    public static ItemGroup? Classify(string itemId)
    {
        var name = StripGame(itemId);
        if (Equipment.Contains(name) || StartsWithAny(name, "SWORD", "TUNIC_", "BOOTS_"))
        {
            return ItemGroup.Equipment;
        }

        if (name == "STRAY_FAIRY_TOWN" || name == "TRANSCENDENT_FAIRY" || name == "PLATINUM_TOKEN"
            || name.StartsWith("GS_TOKEN", StringComparison.Ordinal))
        {
            return ItemGroup.Tokens;
        }

        if (Hidden.Contains(name) || HiddenPrefixes.Any(p => name.StartsWith(p, StringComparison.Ordinal)))
        {
            return null;
        }

        if (StartsWithAny(name, "STONE_", "MEDALLION_", "REMAINS_", "COIN_", "TRIFORCE"))
        {
            return ItemGroup.Quest;
        }

        if (Usable.Contains(name) || StartsWithAny(name, "ARROW_", "SPELL_"))
        {
            return ItemGroup.Items;
        }

        if (name.StartsWith("MASK_", StringComparison.Ordinal))
        {
            return ItemGroup.Masks;
        }

        if (StartsWithAny(name, "BOTTLE_", "BOTTLED_"))
        {
            return ItemGroup.Bottles;
        }

        if (Trade.Contains(name) || name.StartsWith("DEED_", StringComparison.Ordinal))
        {
            return ItemGroup.Trade;
        }

        if (name.StartsWith("BUTTON_", StringComparison.Ordinal))
        {
            return ItemGroup.Buttons;
        }

        if (StartsWithAny(name, "OWL_", "CLOCK", "WORLD_MAP_"))
        {
            return ItemGroup.Travel;
        }

        return ItemGroup.Other;
    }

    public static string StripGame(string itemId)
    {
        foreach (var prefix in new[] { "SHARED_", "OOT_", "MM_" })
        {
            if (itemId.StartsWith(prefix, StringComparison.Ordinal))
            {
                return itemId[prefix.Length..];
            }
        }

        return itemId;
    }

    private static bool StartsWithAny(string name, params string[] prefixes) =>
        prefixes.Any(p => name.StartsWith(p, StringComparison.Ordinal));
}
