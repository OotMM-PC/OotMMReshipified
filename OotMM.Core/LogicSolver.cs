using System.Text.Json;

namespace OotMM.Core;

public sealed class LogicSolver
{
    private const uint OotTimeAll = 0x7;
    private const uint OotTimeDay = 1u << 0;
    private const uint OotTimeNight = 1u << 1;
    private const int MmSliceCount = 46;
    private const uint MaskMmTime2 = (1u << (MmSliceCount - 32)) - 1u;

    private enum Op : byte
    {
        True, False, And, Or, Age, Has, Renewable, License, Event, Masks,
        Special, TimeOot, TimeMm, Price, SongEventOot, SongEventMm, FlagOn, FlagOff,
    }

    private sealed class Node
    {
        public Op Op;
        public int A;
        public int B;
        public uint U1;
        public uint U2;
        public int Item = -1;
        public int Event = -1;
        public int Special = -1;
        public List<int> Kids = [];
    }

    private sealed class SpecialDef
    {
        public List<int> Items = [];
        public List<int> ItemsUnique = [];
        public int Count;
    }

    private sealed class Area
    {
        public bool Mm;
        public int Time;
        public bool AgeChange;
        public List<(int Area, int Expr)> Exits = [];
        public List<(int Location, int Expr)> Locations = [];
        public List<(int Event, int Expr)> Events = [];
        public List<int> Stay = [];
    }

    private record struct AreaData(uint OotTime, uint MmTime, uint MmTime2, uint FlagsOn, uint FlagsOff);

    private record struct Restrictions(uint OotTime, uint MmTime, uint MmTime2, uint FlagsOn, uint FlagsOff);

    private record struct EvalResult(bool Result, bool Restricted, Restrictions R);

    private readonly List<Node> _nodes = [];
    private readonly List<Area> _areas = [];
    private readonly List<string> _locationNames = [];
    private readonly List<string> _itemNames = [];
    private readonly List<string> _eventNames = [];
    private readonly Dictionary<string, int> _itemIndex = [];
    private readonly Dictionary<string, int> _eventIndex = [];
    private readonly List<SpecialDef> _specials = [];
    private readonly List<int> _masksRegular = [];
    private readonly List<int> _prices = [];
    private readonly List<int> _songEventsOot = [];
    private readonly List<int> _songEventsMm = [];
    private readonly HashSet<string> _renewableLocations = [];
    private readonly HashSet<string> _licenseLocations = [];
    private int _spawnArea = -1;
    private int _timeTravelEvent = -1;

    public bool Loaded { get; private set; }

    public string LogicMode { get; private set; } = "";

    public bool IsRenewableLocation(string locationId) => _renewableLocations.Contains(locationId);

    public bool IsLicenseLocation(string locationId) => _licenseLocations.Contains(locationId);

    public sealed class Input
    {
        public Dictionary<string, int> Items { get; } = [];
        public Dictionary<string, int> Renewables { get; } = [];
        public Dictionary<string, int> Licenses { get; } = [];
    }

    public bool LoadFromSeedFile(string seedPath)
    {
        Loaded = false;
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(seedPath));
            if (doc.RootElement.ValueKind != JsonValueKind.Object
                || !doc.RootElement.TryGetProperty("logic", out var logic)
                || logic.ValueKind != JsonValueKind.Object
                || !logic.TryGetProperty("version", out var version)
                || version.ValueKind != JsonValueKind.Number
                || version.GetDouble() != 2)
            {
                return false;
            }

            return LoadLogic(logic);
        }
        catch (Exception ex) when (ex is IOException or JsonException
                                       or InvalidOperationException or KeyNotFoundException)
        {
            return false;
        }
    }

    private bool LoadLogic(JsonElement logic)
    {
        if (logic.TryGetProperty("logicMode", out var mode) && mode.ValueKind == JsonValueKind.String)
        {
            LogicMode = mode.GetString()!;
        }

        var specialIndex = new Dictionary<string, int>();
        if (logic.TryGetProperty("specials", out var specials) && specials.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in specials.EnumerateObject())
            {
                var special = new SpecialDef();
                if (property.Value.TryGetProperty("items", out var items))
                {
                    special.Items.AddRange(items.EnumerateArray().Select(i => InternItem(i.GetString()!)));
                }

                if (property.Value.TryGetProperty("itemsUnique", out var unique))
                {
                    special.ItemsUnique.AddRange(unique.EnumerateArray().Select(i => InternItem(i.GetString()!)));
                }

                if (property.Value.TryGetProperty("count", out var count))
                {
                    special.Count = (int)count.GetDouble();
                }

                specialIndex[property.Name] = _specials.Count;
                _specials.Add(special);
            }
        }

        if (logic.TryGetProperty("masksRegular", out var masks) && masks.ValueKind == JsonValueKind.Array)
        {
            _masksRegular.AddRange(masks.EnumerateArray().Select(i => InternItem(i.GetString()!)));
        }

        if (logic.TryGetProperty("prices", out var prices) && prices.ValueKind == JsonValueKind.Array)
        {
            _prices.AddRange(prices.EnumerateArray()
                .Select(p => p.ValueKind == JsonValueKind.Number ? (int)p.GetDouble() : 0));
        }

        if (!ReadSongEvents(logic, "songEventsOot", 18, _songEventsOot)
            || !ReadSongEvents(logic, "songEventsMm", 13, _songEventsMm))
        {
            return false;
        }

        if (logic.TryGetProperty("renewableLocations", out var renewable)
            && renewable.ValueKind == JsonValueKind.Array)
        {
            foreach (var loc in renewable.EnumerateArray())
            {
                _renewableLocations.Add(loc.GetString()!);
            }
        }

        if (logic.TryGetProperty("licenseLocations", out var license)
            && license.ValueKind == JsonValueKind.Array)
        {
            foreach (var loc in license.EnumerateArray())
            {
                _licenseLocations.Add(loc.GetString()!);
            }
        }

        if (!logic.TryGetProperty("exprs", out var exprs) || exprs.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        foreach (var raw in exprs.EnumerateArray())
        {
            var op = raw[0].GetString()!;
            var node = new Node();
            switch (op)
            {
                case "t": node.Op = Op.True; break;
                case "f": node.Op = Op.False; break;
                case "and":
                case "or":
                    node.Op = op == "and" ? Op.And : Op.Or;
                    node.Kids.AddRange(raw[1].EnumerateArray().Select(k => (int)k.GetDouble()));
                    break;
                case "age": node.Op = Op.Age; node.A = (int)raw[1].GetDouble(); break;
                case "has":
                    node.Op = Op.Has;
                    node.Item = InternItem(raw[1].GetString()!);
                    node.A = (int)raw[2].GetDouble();
                    break;
                case "renew": node.Op = Op.Renewable; node.Item = InternItem(raw[1].GetString()!); break;
                case "lic": node.Op = Op.License; node.Item = InternItem(raw[1].GetString()!); break;
                case "ev": node.Op = Op.Event; node.Event = InternEvent(raw[1].GetString()!); break;
                case "masks": node.Op = Op.Masks; node.A = (int)raw[1].GetDouble(); break;
                case "special":
                    node.Op = Op.Special;
                    if (!specialIndex.TryGetValue(raw[1].GetString()!, out var specialId))
                    {
                        return false;
                    }

                    node.Special = specialId;
                    break;
                case "oottime": node.Op = Op.TimeOot; node.U1 = (uint)raw[1].GetDouble(); break;
                case "mmtime":
                    node.Op = Op.TimeMm;
                    node.U1 = (uint)raw[1].GetDouble();
                    node.U2 = (uint)raw[2].GetDouble();
                    break;
                case "price":
                    node.Op = Op.Price;
                    node.A = (int)raw[1].GetDouble();
                    node.B = (int)raw[2].GetDouble();
                    break;
                case "songevoot":
                case "songevmm":
                    node.Op = op == "songevoot" ? Op.SongEventOot : Op.SongEventMm;
                    node.A = (int)raw[1].GetDouble();
                    node.B = (int)raw[2].GetDouble();
                    break;
                case "flagon":
                case "flagoff":
                    if (!raw[1].TryGetInt32(out var flag) || flag is < 0 or > 31)
                    {
                        return false;
                    }

                    node.Op = op == "flagon" ? Op.FlagOn : Op.FlagOff;
                    node.U1 = 1u << flag;
                    break;
                default:
                    return false;
            }

            _nodes.Add(node);
        }

        if (!logic.TryGetProperty("areas", out var areas) || areas.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        var areaIndex = new Dictionary<string, int>();
        var locationIndex = new Dictionary<string, int>();
        foreach (var property in areas.EnumerateObject())
        {
            areaIndex[property.Name] = areaIndex.Count;
            _areas.Add(new Area());
        }

        foreach (var property in areas.EnumerateObject())
        {
            var area = _areas[areaIndex[property.Name]];
            var value = property.Value;
            area.Mm = ReadInt(value, "g") == 1;
            area.Time = ReadInt(value, "t");
            area.AgeChange = ReadInt(value, "a") == 1;

            if (value.TryGetProperty("x", out var exits) && exits.ValueKind == JsonValueKind.Object)
            {
                foreach (var exit in exits.EnumerateObject())
                {
                    if (areaIndex.TryGetValue(exit.Name, out var target))
                    {
                        area.Exits.Add((target, (int)exit.Value.GetDouble()));
                    }
                }
            }

            if (value.TryGetProperty("l", out var locations) && locations.ValueKind == JsonValueKind.Object)
            {
                foreach (var location in locations.EnumerateObject())
                {
                    if (!locationIndex.TryGetValue(location.Name, out var locId))
                    {
                        locId = _locationNames.Count;
                        locationIndex[location.Name] = locId;
                        _locationNames.Add(location.Name);
                    }

                    area.Locations.Add((locId, (int)location.Value.GetDouble()));
                }
            }

            if (value.TryGetProperty("e", out var events) && events.ValueKind == JsonValueKind.Object)
            {
                foreach (var evt in events.EnumerateObject())
                {
                    area.Events.Add((InternEvent(evt.Name), (int)evt.Value.GetDouble()));
                }
            }

            if (value.TryGetProperty("s", out var stay) && stay.ValueKind == JsonValueKind.Array)
            {
                area.Stay.AddRange(stay.EnumerateArray().Select(s => (int)s.GetDouble()));
            }
        }

        if (!areaIndex.TryGetValue("OOT SPAWN", out _spawnArea))
        {
            return false;
        }

        _timeTravelEvent = InternEvent("OOT_TIME_TRAVEL_AT_WILL");
        Loaded = true;
        return true;
    }

    public HashSet<string> Solve(Input input, ISet<string>? outEvents = null)
    {
        var reachable = new HashSet<string>();
        if (!Loaded)
        {
            return reachable;
        }

        if (LogicMode == "none")
        {
            reachable.UnionWith(_locationNames);
            return reachable;
        }

        var items = new int[_itemNames.Count];
        var renewables = new int[_itemNames.Count];
        var licenses = new int[_itemNames.Count];
        var events = new bool[_eventNames.Count];
        Fill(input.Items, items);
        Fill(input.Renewables, renewables);
        Fill(input.Licenses, licenses);

        var age = 0;
        var areaData = default(AreaData);

        EvalResult Eval(int index)
        {
            var node = _nodes[index];
            switch (node.Op)
            {
                case Op.True:
                    return new EvalResult(true, false, default);
                case Op.False:
                    return new EvalResult(false, false, default);
                case Op.And:
                {
                    var r = default(Restrictions);
                    var restricted = false;
                    foreach (var kid in node.Kids)
                    {
                        var kr = Eval(kid);
                        if (!kr.Result)
                        {
                            return new EvalResult(false, false, default);
                        }

                        if (kr.Restricted)
                        {
                            restricted = true;
                            r = new Restrictions(
                                r.OotTime | kr.R.OotTime, r.MmTime | kr.R.MmTime,
                                r.MmTime2 | kr.R.MmTime2, r.FlagsOn | kr.R.FlagsOn,
                                r.FlagsOff | kr.R.FlagsOff);
                        }
                    }

                    if (restricted && IsRestrictionImpossible(r))
                    {
                        return new EvalResult(false, false, default);
                    }

                    return !restricted || r == default
                        ? new EvalResult(true, false, default)
                        : new EvalResult(true, true, r);
                }

                case Op.Or:
                {
                    var r = new Restrictions(OotTimeAll, 0xffffffffu, 0xffffffffu, 0xffffffffu, 0xffffffffu);
                    var any = false;
                    foreach (var kid in node.Kids)
                    {
                        var kr = Eval(kid);
                        if (!kr.Result)
                        {
                            continue;
                        }

                        if (!kr.Restricted)
                        {
                            return new EvalResult(true, false, default);
                        }

                        any = true;
                        r = new Restrictions(
                            r.OotTime & kr.R.OotTime, r.MmTime & kr.R.MmTime,
                            r.MmTime2 & kr.R.MmTime2, r.FlagsOn & kr.R.FlagsOn,
                            r.FlagsOff & kr.R.FlagsOff);
                    }

                    if (!any)
                    {
                        return new EvalResult(false, false, default);
                    }

                    return r == default
                        ? new EvalResult(true, false, default)
                        : new EvalResult(true, true, r);
                }

                case Op.Age:
                    return new EvalResult(age == node.A, false, default);
                case Op.Has:
                    return new EvalResult(items[node.Item] >= node.A, false, default);
                case Op.Renewable:
                    return new EvalResult(renewables[node.Item] > 0, false, default);
                case Op.License:
                    return new EvalResult(licenses[node.Item] > 0, false, default);
                case Op.Event:
                    return new EvalResult(events[node.Event], false, default);
                case Op.Masks:
                    return new EvalResult(_masksRegular.Sum(i => items[i]) >= node.A, false, default);
                case Op.Special:
                {
                    var special = _specials[node.Special];
                    var sum = special.Items.Sum(i => items[i])
                              + special.ItemsUnique.Count(i => items[i] > 0);
                    return new EvalResult(sum >= special.Count, false, default);
                }

                case Op.TimeOot:
                    return (areaData.OotTime & node.U1) != 0
                        ? new EvalResult(true, true, new Restrictions(OotTimeAll & ~node.U1, 0, 0, 0, 0))
                        : new EvalResult(false, false, default);
                case Op.TimeMm:
                    return (areaData.MmTime & node.U1) != 0 || (areaData.MmTime2 & node.U2) != 0
                        ? new EvalResult(true, true, new Restrictions(0, ~node.U1, ~node.U2, 0, 0))
                        : new EvalResult(false, false, default);
                case Op.Price:
                {
                    var price = node.A >= 0 && node.A < _prices.Count ? _prices[node.A] : 0;
                    return new EvalResult(price <= node.B, false, default);
                }

                case Op.SongEventOot:
                case Op.SongEventMm:
                {
                    var table = node.Op == Op.SongEventOot ? _songEventsOot : _songEventsMm;
                    var value = node.A >= 0 && node.A < table.Count ? table[node.A] : 0;
                    return new EvalResult(value == node.B, false, default);
                }

                case Op.FlagOn:
                    return (areaData.FlagsOff & node.U1) != 0
                        ? new EvalResult(false, false, default)
                        : new EvalResult(true, true, new Restrictions(0, 0, 0, node.U1, 0));
                case Op.FlagOff:
                    return (areaData.FlagsOn & node.U1) != 0
                        ? new EvalResult(false, false, default)
                        : new EvalResult(true, true, new Restrictions(0, 0, 0, 0, node.U1));
                default:
                    return new EvalResult(false, false, default);
            }
        }

        var reachedLoc = new bool[_locationNames.Count];
        var eventsChanged = true;
        var guard = 0;
        while (eventsChanged && guard++ < 512)
        {
            eventsChanged = false;
            Array.Fill(reachedLoc, false);
            var reached = new[] { new AreaData?[_areas.Count], new AreaData?[_areas.Count] };

            var queue = new Queue<(int Age, int Area, AreaData Data, int FromArea)>();
            var init = new AreaData(0, 1, 0, 0, 0);
            queue.Enqueue((0, _spawnArea, init, _spawnArea));
            queue.Enqueue((1, _spawnArea, init, _spawnArea));

            while (queue.Count > 0)
            {
                var entry = queue.Dequeue();
                var area = _areas[entry.Area];
                ref var slot = ref reached[entry.Age][entry.Area];

                var newData = slot is { } existing ? Merge(existing, entry.Data) : entry.Data;

                if (!area.Mm)
                {
                    newData.OotTime |= area.Time switch
                    {
                        1 => OotTimeDay,
                        2 => OotTimeNight,
                        3 => OotTimeAll,
                        _ => 0u,
                    };
                }
                else
                {
                    if (newData.MmTime == 0 && newData.MmTime2 == 0)
                    {
                        continue;
                    }

                    if (area.Stay.Count == 0)
                    {
                        uint mmTime, mmTime2;
                        if (newData.MmTime != 0)
                        {
                            mmTime = newData.MmTime;
                            mmTime |= mmTime << 1;
                            mmTime |= mmTime << 2;
                            mmTime |= mmTime << 4;
                            mmTime |= mmTime << 8;
                            mmTime |= mmTime << 16;
                            mmTime2 = 0xffffffffu;
                        }
                        else
                        {
                            mmTime2 = newData.MmTime2;
                            mmTime2 |= mmTime2 << 1;
                            mmTime2 |= mmTime2 << 2;
                            mmTime2 |= mmTime2 << 4;
                            mmTime2 |= mmTime2 << 8;
                            mmTime2 |= mmTime2 << 16;
                            mmTime = 0;
                        }

                        newData.MmTime = mmTime;
                        newData.MmTime2 = mmTime2 & MaskMmTime2;
                    }
                    else
                    {
                        age = entry.Age;
                        areaData = slot ?? default;
                        var waitMode = false;
                        for (var i = 0; i < MmSliceCount && i < area.Stay.Count; i++)
                        {
                            var mask1 = i < 32 ? 1u << i : 0;
                            var mask2 = i < 32 ? 0 : 1u << (i - 32);
                            if ((newData.MmTime & mask1) != 0 || (newData.MmTime2 & mask2) != 0)
                            {
                                waitMode = true;
                            }
                            else if (waitMode)
                            {
                                if (Eval(area.Stay[i]).Result)
                                {
                                    newData.MmTime |= mask1;
                                    newData.MmTime2 |= mask2;
                                }
                                else
                                {
                                    waitMode = false;
                                }
                            }
                        }
                    }
                }

                if (_timeTravelEvent >= 0 && events[_timeTravelEvent] && area.AgeChange
                    && entry.Area != entry.FromArea)
                {
                    queue.Enqueue((entry.Age ^ 1, entry.Area, newData, entry.Area));
                }

                if (slot is { } covered && Covers(covered, newData))
                {
                    continue;
                }

                slot = newData;
                age = entry.Age;
                areaData = newData;

                foreach (var (locIndex, exprIndex) in area.Locations)
                {
                    if (!reachedLoc[locIndex] && Eval(exprIndex).Result)
                    {
                        reachedLoc[locIndex] = true;
                    }
                }

                foreach (var (eventIndex, exprIndex) in area.Events)
                {
                    if (!events[eventIndex] && Eval(exprIndex).Result)
                    {
                        events[eventIndex] = true;
                        eventsChanged = true;
                    }
                }

                foreach (var (targetArea, exprIndex) in area.Exits)
                {
                    var exit = Eval(exprIndex);
                    if (!exit.Result)
                    {
                        continue;
                    }

                    var exitData = newData;
                    if (exit.Restricted)
                    {
                        exitData.OotTime &= ~exit.R.OotTime;
                        exitData.MmTime &= ~exit.R.MmTime;
                        exitData.MmTime2 &= ~exit.R.MmTime2;
                        exitData.FlagsOn |= exit.R.FlagsOn;
                        exitData.FlagsOff |= exit.R.FlagsOff;
                    }

                    if (!area.Mm && _areas[targetArea].Mm)
                    {
                        exitData.MmTime |= 1;
                    }

                    var targetSlot = reached[entry.Age][targetArea];
                    if (targetSlot is { } t && Covers(t, exitData) && _areas[targetArea].Stay.Count == 0)
                    {
                        continue;
                    }

                    queue.Enqueue((entry.Age, targetArea, exitData, entry.Area));
                }
            }
        }

        for (var i = 0; i < reachedLoc.Length; i++)
        {
            if (reachedLoc[i])
            {
                reachable.Add(_locationNames[i]);
            }
        }

        if (outEvents is not null)
        {
            for (var i = 0; i < events.Length; i++)
            {
                if (events[i])
                {
                    outEvents.Add(_eventNames[i]);
                }
            }
        }

        return reachable;
    }

    private void Fill(Dictionary<string, int> source, int[] destination)
    {
        foreach (var (id, count) in source)
        {
            if (_itemIndex.TryGetValue(id, out var index))
            {
                destination[index] += count;
            }
        }
    }

    private static AreaData Merge(AreaData a, AreaData b) => new(
        a.OotTime | b.OotTime, a.MmTime | b.MmTime, a.MmTime2 | b.MmTime2,
        a.FlagsOn & b.FlagsOn, a.FlagsOff & b.FlagsOff);

    private static bool Covers(AreaData a, AreaData b) =>
        (a.OotTime | b.OotTime) == a.OotTime
        && (a.MmTime | b.MmTime) == a.MmTime
        && (a.MmTime2 | b.MmTime2) == a.MmTime2
        && (a.FlagsOn & b.FlagsOn) == a.FlagsOn
        && (a.FlagsOff & b.FlagsOff) == a.FlagsOff;

    private static bool IsRestrictionImpossible(Restrictions r) =>
        r.OotTime == OotTimeAll
        || (r.MmTime == 0xffffffffu && r.MmTime2 == 0xffffffffu)
        || (r.FlagsOn & r.FlagsOff) != 0;

    private static bool ReadSongEvents(JsonElement logic, string key, int required, List<int> output)
    {
        if (!logic.TryGetProperty(key, out var songs) || songs.ValueKind != JsonValueKind.Array
            || songs.GetArrayLength() != required)
        {
            return false;
        }

        foreach (var song in songs.EnumerateArray())
        {
            if (song.ValueKind != JsonValueKind.Number)
            {
                return false;
            }

            var value = song.GetDouble();
            if (value is < 0 or > 19)
            {
                return false;
            }

            output.Add((int)value);
        }

        return true;
    }

    private static int ReadInt(JsonElement value, string key) =>
        value.TryGetProperty(key, out var field) && field.ValueKind == JsonValueKind.Number
            ? (int)field.GetDouble()
            : 0;

    private int InternItem(string id)
    {
        if (!_itemIndex.TryGetValue(id, out var index))
        {
            index = _itemNames.Count;
            _itemNames.Add(id);
            _itemIndex[id] = index;
        }

        return index;
    }

    private int InternEvent(string id)
    {
        if (!_eventIndex.TryGetValue(id, out var index))
        {
            index = _eventNames.Count;
            _eventNames.Add(id);
            _eventIndex[id] = index;
        }

        return index;
    }
}
