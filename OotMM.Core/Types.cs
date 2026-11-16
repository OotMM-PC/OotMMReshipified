namespace OotMM.Core;

public sealed record ItemRef(Game Game, string Id, string Name);

public sealed record CheckRef(
    Game Game,
    string Id,
    string Name,
    IReadOnlyList<string> Aliases,
    uint? SceneId,
    string Type,
    uint? Flag);

public sealed record ItemPlacement(
    CheckRef Check,
    ItemRef Item,
    ItemRef? Cloak,
    ushort OwnerPlayer,
    ushort SourcePlayer,
    bool Major)
{
    /// <summary>Whether the seed's analysis found this check required to finish; null when the seed carries no such data.</summary>
    public bool? Required { get; init; }

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

public sealed record CrossGameTransition(
    Game FromGame,
    Game ToGame,
    string ToEntrance,
    uint? ToNativeId,
    uint? OotAge);
