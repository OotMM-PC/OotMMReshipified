namespace OotMM.Core;

/// <summary>An item as the generator names it, tagged with its home game.</summary>
public sealed record ItemRef(Game Game, string Id, string Name);

/// <summary>A check location; aliases are alternate ids the generator may emit for it.</summary>
public sealed record CheckRef(
    Game Game,
    string Id,
    string Name,
    IReadOnlyList<string> Aliases,
    uint? SceneId,
    string Type,
    uint? Flag);

/// <summary>One check-to-item assignment; Cloak is the decoy shown before collection.</summary>
public sealed record ItemPlacement(
    CheckRef Check,
    ItemRef Item,
    ItemRef? Cloak,
    ushort OwnerPlayer,
    ushort SourcePlayer,
    bool Major)
{
    public bool IsFor(ushort player) => OwnerPlayer == 0 || OwnerPlayer == player;
}

public sealed record EntranceMapping(
    Game FromGame,
    string From,
    uint? FromNativeId,
    Game ToGame,
    string To,
    uint? ToNativeId)
{
    public bool IsCrossGame => FromGame != ToGame;
}

public sealed record StartingItem(ItemRef Item, ushort Count);

/// <summary>A cross-game load-zone handoff: exit one game, boot the other at an entrance.</summary>
public sealed record CrossGameTransition(
    Game FromGame,
    Game ToGame,
    string ToEntrance,
    uint? ToNativeId,
    uint? OotAge);
