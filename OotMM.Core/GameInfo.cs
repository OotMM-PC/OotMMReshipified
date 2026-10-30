namespace OotMM.Core;

public static class GameInfo
{
    public static string Title(Game game) =>
        game == Game.Oot ? "Ocarina of Time" : "Majora's Mask";

    public static Game FromSeedTag(string tag) =>
        tag.Equals("mm", StringComparison.OrdinalIgnoreCase) ? Game.Mm : Game.Oot;

    public static string ToSeedTag(Game game) => game == Game.Mm ? "mm" : "oot";

    public static Game? TryFromSeedTag(string tag) => tag switch
    {
        "oot" or "OoT" or "OOT" => Game.Oot,
        "mm" or "MM" => Game.Mm,
        _ => null,
    };
}
