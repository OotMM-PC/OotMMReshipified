namespace OotMM.Core;

public static class ProgressiveItemNames
{
    private static readonly string[] ClockNames =
    [
        "Clock (Day 1)",
        "Clock (Night 1)",
        "Clock (Day 2)",
        "Clock (Night 2)",
        "Clock (Day 3)",
        "Clock (Night 3)",
    ];

    public static ItemPlacement? ResolveCheck(
        Seed seed,
        Game checkGame,
        string checkId,
        IReadOnlyDictionary<string, uint> recipientInventory)
    {
        var placement = seed.FindPlacement(checkGame, checkId);
        if (placement is null)
        {
            return null;
        }
        var item = ResolveItem(seed, placement.Item.Id, recipientInventory);
        return item is null ? null : placement with { Item = item };
    }

    public static ItemRef? ResolveItem(
        Seed seed,
        string itemId,
        IReadOnlyDictionary<string, uint> recipientInventory)
    {
        var item = seed.Placements.Select(placement => placement.Item)
            .Concat(seed.StartingItems.Select(starting => starting.Item))
            .FirstOrDefault(candidate => string.Equals(candidate.Id, itemId, StringComparison.Ordinal));
        return item is null
            ? null
            : item with { Name = Resolve(seed, item.Game, item, recipientInventory) };
    }

    public static string Resolve(
        Seed recipientSeed,
        Game destinationGame,
        ItemRef item,
        IReadOnlyDictionary<string, uint> recipientInventory)
    {
        if (TryResolveClock(
                recipientSeed, item.Id, recipientInventory, out var clockName))
        {
            return clockName;
        }

        var suffix = StripPrefix(item.Id);
        var priorCount = PriorCount(destinationGame, suffix, recipientInventory);
        IReadOnlyList<string>? tiers = suffix switch
        {
            "BOMB_BAG" => ["Bomb Bag", "Big Bomb Bag", "Biggest Bomb Bag"],
            "BOMBCHU_BAG" => ["Bombchu Bag", "Big Bombchu Bag", "Biggest Bombchu Bag"],
            "BOW" => destinationGame == Game.Oot
                ? ["Fairy Bow", "Big Quiver", "Biggest Quiver"]
                : ["Hero's Bow", "Big Quiver", "Biggest Quiver"],
            "SLINGSHOT" => ["Fairy Slingshot", "Large Bullet Bag", "Largest Bullet Bag"],
            "HOOKSHOT" => HookshotTiers(recipientSeed, item),
            "OCARINA" => ["Fairy Ocarina", "Ocarina of Time"],
            "WALLET" => WalletTiers(recipientSeed, destinationGame),
            "SCALE" => ScaleTiers(recipientSeed),
            "STRENGTH" => ["Goron's Bracelet", "Silver Gauntlets", "Golden Gauntlets"],
            "MAGIC_UPGRADE" => ["Magic Upgrade", "Larger Magic Upgrade"],
            "SWORD" => SwordTiers(recipientSeed, destinationGame),
            "SWORD_GORON" => ["Giant's Knife", "Biggoron's Sword"],
            "SHIELD" => destinationGame == Game.Oot
                ? ["Deku Shield", "Hylian Shield", "Mirror Shield"]
                : ["Hero's Shield", "Mirror Shield"],
            "STICK_UPGRADE" => ["Deku Stick Upgrade", "Second Deku Stick Upgrade"],
            "NUT_UPGRADE" => ["Deku Nut Upgrade", "Second Deku Nut Upgrade"],
            "SONG_GORON_HALF" => ["Goron Lullaby Intro", "Goron Lullaby"],
            _ => null,
        };

        if (tiers is null || tiers.Count == 0)
        {
            return item.Name.Length > 0 ? item.Name : item.Id;
        }

        return tiers[(int)Math.Min(priorCount, (uint)tiers.Count - 1)];
    }

    private static IReadOnlyList<string> HookshotTiers(Seed seed, ItemRef item)
    {
        if (!item.Id.StartsWith("MM_", StringComparison.Ordinal))
        {
            return ["Hookshot", "Longshot"];
        }

        var copies = seed.Placements.Count(placement =>
            placement.OwnerPlayer == seed.PlayerId
            && placement.Item.Id == item.Id);
        return copies >= 2 ? ["Short Hookshot", "Hookshot"] : ["Hookshot"];
    }

    private static IReadOnlyList<string> WalletTiers(Seed seed, Game game)
    {
        var tiers = new List<string>();
        if (seed.GetBoolSetting("childWallets"))
        {
            tiers.Add(game == Game.Oot ? "Child's Wallet" : "Child Wallet");
        }
        tiers.Add(game == Game.Oot ? "Adult's Wallet" : "Adult Wallet");
        tiers.Add(game == Game.Oot ? "Giant's Wallet" : "Giant Wallet");
        if (seed.GetBoolSetting("colossalWallets"))
        {
            tiers.Add("Colossal Wallet");
        }
        if (seed.GetBoolSetting("bottomlessWallets"))
        {
            tiers.Add("Bottomless Wallet");
        }
        return tiers;
    }

    private static IReadOnlyList<string> ScaleTiers(Seed seed)
    {
        var tiers = new List<string>();
        if (seed.GetBoolSetting("bronzeScale"))
        {
            tiers.Add("Bronze Scale");
        }
        tiers.Add("Silver Scale");
        tiers.Add("Golden Scale");
        return tiers;
    }

    private static IReadOnlyList<string> SwordTiers(Seed seed, Game game)
    {
        if (game == Game.Mm)
        {
            return ["Kokiri Sword", "Razor Sword", "Gilded Sword"];
        }
        if (seed.GetBoolSetting("extraChildSwordsOot"))
        {
            return ["Kokiri Sword", "Razor Sword", "Gilded Sword"];
        }
        return ["Kokiri Sword", "Master Sword", "Giant's Knife", "Biggoron's Sword"];
    }

    private static bool TryResolveClock(
        Seed seed,
        string itemId,
        IReadOnlyDictionary<string, uint> inventory,
        out string name)
    {
        for (var index = 0; index < ClockNames.Length; index++)
        {
            if (itemId == $"MM_CLOCK{index + 1}")
            {
                name = ClockNames[index];
                return true;
            }
        }
        if (itemId != "MM_CLOCK")
        {
            name = "";
            return false;
        }

        var mode = seed.GetStringSetting("progressiveClocks", "ascending");
        var descending = mode == "descending";
        byte owned = mode == "separate" ? (byte)0 : descending ? (byte)0x20 : (byte)0x01;
        for (var index = 0; index < ClockNames.Length; index++)
        {
            if (inventory.GetValueOrDefault($"MM_CLOCK{index + 1}") > 0)
            {
                owned |= (byte)(1 << index);
            }
        }

        int ClaimNext()
        {
            if (descending)
            {
                for (var index = ClockNames.Length - 1; index >= 0; index--)
                {
                    if ((owned & (1 << index)) == 0)
                    {
                        owned |= (byte)(1 << index);
                        return index;
                    }
                }
            }
            else
            {
                for (var index = 0; index < ClockNames.Length; index++)
                {
                    if ((owned & (1 << index)) == 0)
                    {
                        owned |= (byte)(1 << index);
                        return index;
                    }
                }
            }
            return -1;
        }

        var prior = inventory.GetValueOrDefault("MM_CLOCK");
        for (var index = 0u; index < prior; index++)
        {
            ClaimNext();
        }
        var granted = ClaimNext();
        name = granted >= 0
            ? ClockNames[granted]
            : ClockNames[descending ? 0 : ClockNames.Length - 1];
        return true;
    }

    private static uint PriorCount(
        Game game,
        string suffix,
        IReadOnlyDictionary<string, uint> inventory)
    {
        var gameId = (game == Game.Oot ? "OOT_" : "MM_") + suffix;
        var sharedId = "SHARED_" + suffix;
        return inventory.GetValueOrDefault(gameId) + inventory.GetValueOrDefault(sharedId);
    }

    private static string StripPrefix(string itemId)
    {
        foreach (var prefix in new[] { "OOT_", "MM_", "SHARED_" })
        {
            if (itemId.StartsWith(prefix, StringComparison.Ordinal))
            {
                return itemId[prefix.Length..];
            }
        }
        return itemId;
    }
}
