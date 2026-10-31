namespace OotMM.Core;

public static class Endgame
{
    public const string Ganon = "ganon";
    public const string Majora = "majora";

    public static readonly IReadOnlyDictionary<string, string> BossItems =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [Ganon] = "OOT_TRIFORCE_FULL",
            [Majora] = "MM_MASK_MAJORA",
        };
}
