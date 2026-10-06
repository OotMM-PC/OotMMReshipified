namespace OotMM.Core;

public static class SharedResourceFamilies
{
    public static readonly IReadOnlyList<string> Settings =
    [
        "sharedBombchu", "sharedWallets", "sharedHealth", "sharedMagic", "sharedBows", "sharedBombBags",
        "sharedNutsSticks", "sharedSlingshot", "sharedPowderKeg", "sharedBottles", "crossGameFw",
        "trapIce", "trapFire", "trapShock", "trapDrain", "trapAntiMagic", "trapKnockback", "trapRupoor",
    ];

    public static readonly IReadOnlyDictionary<string, string> EnumSettings = new Dictionary<string, string>
    {
        ["erBoss"] = "full",
    };

    public static bool AnyEnabled(Seed seed) => Settings.Any(setting => seed.GetBoolSetting(setting))
        || EnumSettings.Any(pair => seed.GetStringSetting(pair.Key) == pair.Value);
}
