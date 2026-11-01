using OotMM.Launcher.Services;
using OotMM.Launcher.ViewModels;

namespace OotMM.Launcher.Tests;

public sealed class ModListTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ootmm-modlist-" + Guid.NewGuid().ToString("N"));
    private readonly GameCardViewModel _card;

    public ModListTests()
    {
        var mods = Path.Combine(_root, "mods");
        Directory.CreateDirectory(mods);
        File.WriteAllBytes(Path.Combine(_root, "soh.exe"), []);
        foreach (var name in new[] { "a.o2r", "b.o2r", "c.o2r", "d.o2r.disabled", "ootmm_assets.o2r" })
        {
            File.WriteAllBytes(Path.Combine(mods, name), []);
        }

        File.WriteAllLines(Path.Combine(mods, "loadorder.txt"), ["a.o2r", "b.o2r", "c.o2r", "d.o2r", "ootmm_assets.o2r"]);
        _card = new GameCardViewModel(isOot: true, new NoPicker(), () => { }) { ExePath = Path.Combine(_root, "soh.exe") };
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private string[] Selected() => _card.Mods.Where(m => m.IsSelected).Select(m => m.Name).ToArray();

    [Fact]
    public void ClickSelectsOnlyThatRowAndModifiersExtend()
    {
        _card.SelectMod(0, toggle: false, extend: false);
        _card.SelectMod(2, toggle: false, extend: false);
        Assert.Equal(["c.o2r"], Selected());

        _card.SelectMod(0, toggle: true, extend: false);
        Assert.Equal(["a.o2r", "c.o2r"], Selected());

        _card.SelectMod(3, toggle: false, extend: true);
        Assert.Equal(["a.o2r", "b.o2r", "c.o2r", "d.o2r"], Selected());

        _card.ClearSelection();
        Assert.False(_card.HasSelection);
    }

    [Fact]
    public void EnablingRenamesOnDiskWithoutRebuildingTheList()
    {
        var before = _card.Mods.ToList();
        _card.SelectMod(1, toggle: false, extend: false);

        _card.SetModEnabled(3, true);

        Assert.True(File.Exists(Path.Combine(_root, "mods", "d.o2r")));
        Assert.True(_card.Mods[3].Enabled);
        Assert.Equal(before, _card.Mods);
        Assert.Equal(["b.o2r"], Selected());

        _card.RefreshMods();
        Assert.Equal(before, _card.Mods);
    }

    [Fact]
    public void DraggingTheSelectionKeepsItsOrder()
    {
        _card.SelectMod(0, toggle: false, extend: false);
        _card.SelectMod(1, toggle: true, extend: false);

        _card.MoveSelectedTo(4);

        Assert.Equal(["c.o2r", "d.o2r", "a.o2r", "b.o2r", "ootmm_assets.o2r"], _card.Mods.Select(m => m.Name));
        var saved = File.ReadAllLines(Path.Combine(_root, "mods", "loadorder.txt")).Where(l => !l.StartsWith('#'));
        Assert.Equal(_card.Mods.Select(m => m.Name), saved);
    }

    [Fact]
    public void ExternalRenameUpdatesTheExistingEntry()
    {
        var entry = _card.Mods[0];
        File.Move(Path.Combine(_root, "mods", "a.o2r"), Path.Combine(_root, "mods", "a.o2r.disabled"));

        _card.RefreshMods();

        Assert.Same(entry, _card.Mods[0]);
        Assert.False(entry.Enabled);
    }

    private sealed class NoPicker : IFilePicker
    {
        public Task<string?> PickFileAsync(string title, string filterName, params string[] patterns) =>
            Task.FromResult<string?>(null);
    }
}
