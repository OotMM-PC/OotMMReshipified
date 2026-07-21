namespace OotMM.Core;

/// <summary>One check in some player's world whose item is addressed to the local player.</summary>
public sealed record ForeignPlacement(ushort SourcePlayer, Game CheckGame, string CheckId, ItemRef Item);

/// <summary>(sourcePlayer, checkGame, checkId) -> item addressed to the local
/// player. Facts from worlds not in CoveredWorlds must defer, never skip.</summary>
public sealed class ForeignPlacementTable
{
    private readonly Dictionary<string, ItemRef> _entries = [];
    private readonly SortedSet<ushort> _coveredWorlds = [];

    public ForeignPlacementTable(Seed localSeed)
    {
        TrapsLink = localSeed.GetBoolSetting("trapsLink");
        AddManifestWorld(localSeed.PlayerId, localSeed, localSeed.PlayerId);
    }

    public ForeignPlacementTable(Seed localSeed, IEnumerable<ForeignPlacement> placements)
        : this(localSeed)
    {
        foreach (var placement in placements)
        {
            AddEntry(placement.SourcePlayer, placement.CheckGame, placement.CheckId, placement.Item);
            AddCoveredWorld(placement.SourcePlayer);
        }
    }

    public ForeignPlacementTable(
        Seed localSeed, IEnumerable<ForeignPlacement> placements, IEnumerable<ushort> coveredWorlds)
        : this(localSeed, placements)
    {
        foreach (var worldPlayer in coveredWorlds)
        {
            AddCoveredWorld(worldPlayer);
        }
    }

    public bool TrapsLink { get; }

    public int Size => _entries.Count;

    public IReadOnlySet<ushort> CoveredWorlds => _coveredWorlds;

    public bool CoversWorld(ushort worldPlayer) => _coveredWorlds.Contains(worldPlayer);

    public void AddCoveredWorld(ushort worldPlayer)
    {
        if (worldPlayer != 0)
        {
            _coveredWorlds.Add(worldPlayer);
        }
    }

    /// <summary>Indexes the world's placements addressed to localPlayer and marks it covered.</summary>
    public void AddManifestWorld(ushort worldPlayer, Seed world, ushort localPlayer)
    {
        AddCoveredWorld(worldPlayer);
        foreach (var placement in world.Placements)
        {
            var linkedTrap = TrapsLink && ItemRules.IsTrapItemId(placement.Item.Id);
            if (placement.OwnerPlayer != localPlayer && placement.OwnerPlayer != 0 && !linkedTrap)
            {
                continue;
            }

            AddEntry(worldPlayer, placement.Check.Game, placement.Check.Id, placement.Item);
            if (placement.Check.Name.Length > 0)
            {
                AddEntry(worldPlayer, placement.Check.Game, placement.Check.Name, placement.Item);
            }

            foreach (var alias in placement.Check.Aliases)
            {
                AddEntry(worldPlayer, placement.Check.Game, alias, placement.Item);
            }
        }
    }

    public ItemRef? Find(ushort sourcePlayer, Game checkGame, string checkId) =>
        _entries.GetValueOrDefault(TableKey(sourcePlayer, checkGame, checkId));

    private void AddEntry(ushort worldPlayer, Game checkGame, string checkId, ItemRef item)
    {
        if (checkId.Length > 0)
        {
            _entries.TryAdd(TableKey(worldPlayer, checkGame, checkId), item);
        }
    }

    private static string TableKey(ushort sourcePlayer, Game checkGame, string checkId) =>
        $"p{sourcePlayer}|{GameInfo.ToSeedTag(checkGame)}|{checkId}";
}
