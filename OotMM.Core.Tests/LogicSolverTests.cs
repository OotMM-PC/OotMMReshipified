using OotMM.Core;
using Xunit;

namespace OotMM.Core.Tests;

// Ported from OOTMMPC tests/ootmm_logic_solver_tests.cpp: pins age gating,
// spawn starting locations, event propagation, and song-event resolution.
public sealed class LogicSolverTests : IDisposable
{
    private const string SeedJson = """
        {
          "logic": {
            "version": 2,
            "logicMode": "allLocations",
            "exprs": [
              ["t"],
              ["age", 0],
              ["age", 1],
              ["ev", "OOT_TIME_TRAVEL"],
              ["and", [1, 3]],
              ["has", "OOT_SWORD_MASTER", 1],
              ["songevoot", 0, 0],
              ["has", "OOT_SONG_ZELDA", 1],
              ["songevoot", 0, 1],
              ["has", "SHARED_SONG_EPONA", 1],
              ["has", "SHARED_BUTTON_C_UP", 1],
              ["has", "SHARED_BUTTON_C_RIGHT", 1],
              ["has", "SHARED_BUTTON_C_LEFT", 1],
              ["and", [6, 7]],
              ["and", [8, 9, 10, 11, 12]],
              ["or", [13, 14]]
            ],
            "areas": {
              "OOT SPAWN": {
                "g": 0, "t": 3, "a": 0,
                "x": { "OOT SPAWN CHILD": 4, "OOT SPAWN ADULT": 2 },
                "l": { "Spawn Free Check": 0 }
              },
              "OOT SPAWN CHILD": { "g": 0, "t": 3, "a": 0, "x": { "Child Area": 0 } },
              "OOT SPAWN ADULT": { "g": 0, "t": 3, "a": 0, "x": { "Adult Area": 0 } },
              "Child Area": { "g": 0, "t": 3, "a": 0, "l": { "Child Only Check": 0 } },
              "Adult Area": {
                "g": 0, "t": 3, "a": 0,
                "x": { "Temple": 0 },
                "l": { "Adult Check": 0, "Tombstone Song Check": 15 }
              },
              "Temple": { "g": 0, "t": 3, "a": 0, "e": { "OOT_TIME_TRAVEL": 5 } }
            },
            "prices": [],
            "songEventsOot": [1,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0],
            "songEventsMm": [0,0,0,0,0,0,0,0,0,0,0,0,0],
            "specials": {},
            "masksRegular": [],
            "renewableLocations": [],
            "licenseLocations": []
          }
        }
        """;

    private readonly string _seedPath =
        Path.Combine(Path.GetTempPath(), $"ootmm-solver-test-{Guid.NewGuid():N}.json");

    private readonly LogicSolver _solver = new();

    public LogicSolverTests()
    {
        File.WriteAllText(_seedPath, SeedJson);
        Assert.True(_solver.LoadFromSeedFile(_seedPath));
    }

    public void Dispose()
    {
        try
        {
            File.Delete(_seedPath);
        }
        catch (IOException)
        {
        }
    }

    [Fact]
    public void AdultStart_NoItems_AgeGatingHolds()
    {
        var reachable = _solver.Solve(new LogicSolver.Input());

        Assert.Contains("Spawn Free Check", reachable);   // skipZelda-style spawn location
        Assert.Contains("Adult Check", reachable);
        Assert.DoesNotContain("Child Only Check", reachable); // no time travel yet
        Assert.DoesNotContain("Tombstone Song Check", reachable);
    }

    [Fact]
    public void MasterSword_PropagatesTimeTravelEvent()
    {
        var input = new LogicSolver.Input();
        input.Items["OOT_SWORD_MASTER"] = 1;
        var events = new HashSet<string>();

        var reachable = _solver.Solve(input, events);

        Assert.Contains("OOT_TIME_TRAVEL", events);
        Assert.Contains("Child Only Check", reachable);
    }

    [Fact]
    public void SongEventTable_ResolvesReassignedSong()
    {
        var vanilla = new LogicSolver.Input();
        vanilla.Items["OOT_SONG_ZELDA"] = 1;
        Assert.DoesNotContain("Tombstone Song Check", _solver.Solve(vanilla));

        var noButtons = new LogicSolver.Input();
        noButtons.Items["SHARED_SONG_EPONA"] = 1;
        Assert.DoesNotContain("Tombstone Song Check", _solver.Solve(noButtons));

        var full = new LogicSolver.Input();
        full.Items["SHARED_SONG_EPONA"] = 1;
        full.Items["SHARED_BUTTON_C_UP"] = 1;
        full.Items["SHARED_BUTTON_C_RIGHT"] = 1;
        full.Items["SHARED_BUTTON_C_LEFT"] = 1;
        Assert.Contains("Tombstone Song Check", _solver.Solve(full));
    }

    [Fact]
    public void TrackerJoin_InLogicIsReachableMinusCollected()
    {
        // The solver's location ids and the placements' checkNames are the same
        // namespace; the collected sword opens the child side.
        var placements = new (string Name, string ItemId)[]
        {
            ("Spawn Free Check", "OOT_RUPEE_GREEN"),
            ("Adult Check", "OOT_SWORD_MASTER"),
            ("Child Only Check", "OOT_RUPEE_BLUE"),
            ("Tombstone Song Check", "OOT_RUPEE_RED"),
        };
        var collected = new HashSet<string> { "Adult Check" };

        var input = new LogicSolver.Input();
        foreach (var (name, itemId) in placements)
        {
            if (collected.Contains(name))
            {
                input.Items[itemId] = input.Items.GetValueOrDefault(itemId) + 1;
            }
        }

        var reachable = _solver.Solve(input);
        var inLogicUncollected = placements
            .Where(p => reachable.Contains(p.Name) && !collected.Contains(p.Name))
            .Select(p => p.Name)
            .ToList();

        Assert.Equal(["Spawn Free Check", "Child Only Check"], inLogicUncollected);
        Assert.All(reachable, loc => Assert.Contains(loc, placements.Select(p => p.Name)));
    }

    [Fact]
    public void LogicModeNone_ReturnsEveryLocation()
    {
        var nonePath = _seedPath + ".none.json";
        File.WriteAllText(nonePath, SeedJson.Replace("\"allLocations\"", "\"none\""));
        var solver = new LogicSolver();
        Assert.True(solver.LoadFromSeedFile(nonePath));

        Assert.Equal(4, solver.Solve(new LogicSolver.Input()).Count);
        File.Delete(nonePath);
    }

    [Fact]
    public void SeedWithoutLogic_DoesNotLoad()
    {
        var noLogic = _seedPath + ".nologic.json";
        File.WriteAllText(noLogic, """{"seedId":"X"}""");
        Assert.False(new LogicSolver().LoadFromSeedFile(noLogic));
        File.Delete(noLogic);
    }
}
