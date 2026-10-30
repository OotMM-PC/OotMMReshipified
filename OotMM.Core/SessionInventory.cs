using System.Text.Json;

namespace OotMM.Core;

public sealed class SessionInventory
{
    public const uint MaxValue = 1_000_000;

    private readonly Seed _seed;
    private readonly HashSet<string> _validItemIds;
    private readonly DebugItemAdjustmentStore _debugAdjustments;
    private Dictionary<string, uint> _baseItems = new(StringComparer.Ordinal);
    private Dictionary<string, uint> _items = new(StringComparer.Ordinal);

    public SessionInventory(Seed seed, string debugAdjustmentPath)
    {
        _seed = seed;
        _validItemIds = seed.Placements.Select(p => p.Item.Id)
            .Concat(seed.StartingItems.Select(i => i.Item.Id))
            .ToHashSet(StringComparer.Ordinal);
        _debugAdjustments = new DebugItemAdjustmentStore(debugAdjustmentPath);
        Reconcile([], new Dictionary<string, int>());
    }

    public ulong Revision { get; private set; }

    public string LastError => _debugAdjustments.LastError;

    public bool IsValid => _debugAdjustments.IsValid;

    public IReadOnlyDictionary<string, uint> Items => _items;

    public bool Reconcile(
        IEnumerable<string> completedChecks,
        IReadOnlyDictionary<string, int> remoteItemCounts)
    {
        var completed = completedChecks.ToHashSet(StringComparer.Ordinal);
        var nextBase = new Dictionary<string, uint>(StringComparer.Ordinal);

        foreach (var startingItem in _seed.StartingItems)
        {
            Add(nextBase, startingItem.Item.Id, startingItem.Count);
        }

        foreach (var placement in _seed.Placements)
        {
            if (placement.IsFor(_seed.PlayerId)
                && completed.Contains(Identity.CheckKey(placement.Check.Game, placement.Check.Id)))
            {
                Add(nextBase, placement.Item.Id, 1);
            }
        }

        foreach (var (itemId, count) in remoteItemCounts)
        {
            if (count > 0)
            {
                Add(nextBase, itemId, (uint)count);
            }
        }

        _baseItems = nextBase;
        return Rebuild();
    }

    public bool TrySetDebugValue(string itemId, uint value)
    {
        if (!_validItemIds.Contains(itemId) || !IsValidItemId(itemId) || value > MaxValue)
        {
            return false;
        }

        var baseValue = _baseItems.GetValueOrDefault(itemId);
        var adjustment = (long)value - baseValue;
        if (!_debugAdjustments.TrySet(itemId, adjustment))
        {
            return false;
        }

        Rebuild();
        return true;
    }

    public bool TryClearDebugAdjustments()
    {
        if (!_debugAdjustments.TryClear())
        {
            return false;
        }

        Rebuild();
        return true;
    }

    public static bool IsValidItemId(string itemId) =>
        itemId is { Length: > 0 and <= 128 }
        && itemId.All(c => char.IsAsciiLetterOrDigit(c) || c == '_');

    private bool Rebuild()
    {
        var next = new Dictionary<string, uint>(_baseItems, StringComparer.Ordinal);
        foreach (var (itemId, adjustment) in _debugAdjustments.Adjustments)
        {
            if (!_validItemIds.Contains(itemId))
            {
                continue;
            }
            var value = (long)next.GetValueOrDefault(itemId) + adjustment;
            if (value <= 0)
            {
                next.Remove(itemId);
            }
            else
            {
                next[itemId] = (uint)Math.Min(value, MaxValue);
            }
        }

        if (DictionariesEqual(_items, next))
        {
            return false;
        }

        _items = next;
        Revision++;
        return true;
    }

    private static void Add(Dictionary<string, uint> items, string itemId, uint amount)
    {
        if (IsValidItemId(itemId))
        {
            items[itemId] = (uint)Math.Min(
                (ulong)MaxValue,
                (ulong)items.GetValueOrDefault(itemId) + amount);
        }
    }

    private static bool DictionariesEqual(
        IReadOnlyDictionary<string, uint> left,
        IReadOnlyDictionary<string, uint> right) =>
        left.Count == right.Count
        && left.All(item => right.GetValueOrDefault(item.Key) == item.Value);
}

internal sealed class DebugItemAdjustmentStore
{
    private const string Format = "ootmm.pc.debug-items.v1";
    private readonly string _path;
    private Dictionary<string, long> _adjustments = new(StringComparer.Ordinal);

    public DebugItemAdjustmentStore(string path)
    {
        _path = path;
        if (File.Exists(path))
        {
            Load();
        }
    }

    public string LastError { get; private set; } = "";

    public bool IsValid => LastError.Length == 0;

    public IReadOnlyDictionary<string, long> Adjustments => _adjustments;

    public bool TrySet(string itemId, long adjustment)
    {
        if (!SessionInventory.IsValidItemId(itemId)
            || adjustment is < -(long)SessionInventory.MaxValue or > SessionInventory.MaxValue)
        {
            return false;
        }

        var next = new Dictionary<string, long>(_adjustments, StringComparer.Ordinal);
        if (adjustment == 0)
        {
            next.Remove(itemId);
        }
        else
        {
            next[itemId] = adjustment;
        }

        if (!Persist(next))
        {
            LastError = $"cannot persist debug item adjustments {_path}";
            return false;
        }

        _adjustments = next;
        LastError = "";
        return true;
    }

    public bool TryClear()
    {
        try
        {
            File.Delete(_path);
            _adjustments.Clear();
            LastError = "";
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            LastError = $"cannot clear debug item adjustments {_path}: {ex.Message}";
            return false;
        }
    }

    private void Load()
    {
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(_path));
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("format", out var format)
                || format.GetString() != Format
                || !root.TryGetProperty("adjustments", out var adjustments)
                || adjustments.ValueKind != JsonValueKind.Object)
            {
                LastError = $"debug item adjustments are malformed: {_path}";
                return;
            }

            var parsed = new Dictionary<string, long>(StringComparer.Ordinal);
            foreach (var item in adjustments.EnumerateObject())
            {
                if (!SessionInventory.IsValidItemId(item.Name)
                    || !item.Value.TryGetInt64(out var value)
                    || value is < -(long)SessionInventory.MaxValue or > SessionInventory.MaxValue)
                {
                    LastError = $"debug item adjustments contain an invalid item: {_path}";
                    return;
                }
                if (value != 0)
                {
                    parsed[item.Name] = value;
                }
            }

            _adjustments = parsed;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            LastError = $"cannot load debug item adjustments {_path}: {ex.Message}";
        }
    }

    private bool Persist(IReadOnlyDictionary<string, long> adjustments)
    {
        var json = JsonSerializer.Serialize(new
        {
            format = Format,
            adjustments,
        });
        return DurableFile.WriteAtomic(_path, json + "\n");
    }
}
