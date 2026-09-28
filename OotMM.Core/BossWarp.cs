namespace OotMM.Core;

public static class BossWarp
{
    private const uint ForeignEntrance = 0x80000000;

    public static bool IsDestinationAllowed(IReadOnlyList<uint> dungeonWarps, Game game, uint nativeId)
    {
        if (game is not (Game.Oot or Game.Mm) || (nativeId & ForeignEntrance) != 0)
        {
            return false;
        }
        var destination = game == Game.Mm ? nativeId | ForeignEntrance : nativeId;
        return dungeonWarps.Any(warp => Matches(warp, destination));
    }

    private static bool Matches(uint warp, uint destination) => warp switch
    {
        0x00000209 => destination == 0x00000457,
        0x00000215 => destination == 0x00000600,
        0x0000024a => destination == 0x000004f6,
        0x0000021d => destination == 0x00000604,
        0x00000205 => destination == 0x00000568,
        0x80008610 => destination is 0x80008600 or 0x80003010,
        0x8000b210 => destination == 0x80009a70,
        0x8000aa20 or 0x8000ac10 => destination == 0x80002070,
        _ => destination == warp,
    };
}
