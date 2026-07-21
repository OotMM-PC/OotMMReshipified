namespace OotMM.Core;

public static class GameInfo
{
    /// <summary>Display title as shown in banners ("Ocarina of Time" / "Majora's Mask").</summary>
    public static string Title(Game game) =>
        game == Game.Oot ? "Ocarina of Time" : "Majora's Mask";

    /// <summary>Parses the seed file's lowercase game tag ("oot" / "mm").</summary>
    public static Game FromSeedTag(string tag) =>
        tag.Equals("mm", StringComparison.OrdinalIgnoreCase) ? Game.Mm : Game.Oot;

    /// <summary>The lowercase tag used in seed files and wire packets.</summary>
    public static string ToSeedTag(Game game) => game == Game.Mm ? "mm" : "oot";

    /// <summary>Strict tag parse (same accepted spellings as the C++ GameFromString).</summary>
    public static Game? TryFromSeedTag(string tag) => tag switch
    {
        "oot" or "OoT" or "OOT" => Game.Oot,
        "mm" or "MM" => Game.Mm,
        _ => null,
    };
}
