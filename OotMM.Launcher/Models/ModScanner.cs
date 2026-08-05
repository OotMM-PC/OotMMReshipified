using System.IO;

namespace OotMM.Launcher.Models;

/// <summary>One archive in a game's mods/ directory as found on disk.</summary>
public sealed record ModInfo(string Name, string FullPath, bool Required, bool EnabledOnDisk);

/// <summary>
/// Lists the mod archives of a game the same way the games themselves do.
/// Semantics ported from the original launcher:
///  - only *.o2r / *.otr files inside "mods" next to the game exe count;
///  - a ".disabled" filename suffix means present-but-disabled;
///  - launcher-built OoTMM assets and seed music are required and forced on, but
///    rank like anything else so a pack can override what they provide;
///  - "zz_coop_*" / "zz_localmodel_*" are launcher-regenerated internals, hidden
///    from the user list;
///  - loadorder.txt ranks listed archives above unlisted ones (top line = highest
///    priority). The list reads top-priority-first, which is the reverse of the
///    order the games mount in, because a later mount overrides an earlier one.
/// </summary>
public static class ModScanner
{
    private const string DisabledSuffix = ".disabled";
    private const string LoadOrderFile = "loadorder.txt";

    public static bool IsRequired(string lowerName) =>
        lowerName is "ootmm_assets.o2r" or "ootmm_seed_music.o2r";

    private static bool IsLauncherGenerated(string name) =>
        name.StartsWith("zz_coop_", StringComparison.Ordinal)
        || name.StartsWith("zz_localmodel_", StringComparison.Ordinal);

    public static IReadOnlyList<ModInfo> Scan(string exePath)
    {
        var exeDir = Path.GetDirectoryName(exePath);
        if (string.IsNullOrEmpty(exeDir))
        {
            return [];
        }

        var modsDir = Path.Combine(exeDir, "mods");
        if (!Directory.Exists(modsDir))
        {
            return [];
        }

        var mods = new List<ModInfo>();
        foreach (var file in Directory.EnumerateFiles(modsDir))
        {
            var fileName = Path.GetFileName(file);
            var enabled = true;
            var display = fileName;
            if (fileName.EndsWith(DisabledSuffix, StringComparison.Ordinal))
            {
                enabled = false;
                display = fileName[..^DisabledSuffix.Length];
            }

            var lower = display.ToLowerInvariant();
            if (!lower.EndsWith(".o2r", StringComparison.Ordinal)
                && !lower.EndsWith(".otr", StringComparison.Ordinal))
            {
                continue;
            }

            if (IsLauncherGenerated(display))
            {
                continue;
            }

            var required = IsRequired(lower);
            mods.Add(new ModInfo(display, file, required, required || enabled));
        }

        var order = LoadLoadOrder(modsDir);

        int RankOf(ModInfo mod)
        {
            var stem = Path.GetFileNameWithoutExtension(mod.Name);
            for (var i = 0; i < order.Count; i++)
            {
                if (string.Equals(order[i], mod.Name, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(order[i], stem, StringComparison.OrdinalIgnoreCase))
                {
                    return i;
                }
            }

            return -1;
        }

        // Listed archives rank above unlisted ones. Unlisted fall back to the games' own
        // tie-break, which is alphabetical mount order, so the last name alphabetically ends
        // up highest and the list shows them reversed.
        return mods
            .Select(mod => (mod, rank: RankOf(mod)))
            .OrderBy(x => x.rank < 0 ? 1 : 0)
            .ThenBy(x => x.rank)
            .ThenByDescending(x => x.mod.Name, StringComparer.Ordinal)
            .Select(x => x.mod)
            .ToList();
    }

    /// <summary>Enables/disables an archive on disk via the ".disabled" filename
    /// rename the games understand. Disk is the sole source of truth.</summary>
    public static bool SetEnabledOnDisk(ModInfo mod, bool enabled)
    {
        if (mod.Required)
        {
            return true;
        }

        var basePath = mod.FullPath.EndsWith(DisabledSuffix, StringComparison.Ordinal)
            ? mod.FullPath[..^DisabledSuffix.Length]
            : mod.FullPath;
        var target = enabled ? basePath : basePath + DisabledSuffix;
        if (target == mod.FullPath)
        {
            return true;
        }

        try
        {
            File.Move(mod.FullPath, target);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>Writes loadorder.txt (top line = highest priority) atomically;
    /// same header and format as the original launcher so hand-edits and the
    /// C++ build stay interchangeable.</summary>
    public static bool SaveLoadOrder(string exePath, IEnumerable<string> namesTopFirst)
    {
        var exeDir = Path.GetDirectoryName(exePath);
        if (string.IsNullOrEmpty(exeDir))
        {
            return false;
        }

        var modsDir = Path.Combine(exeDir, "mods");
        if (!Directory.Exists(modsDir))
        {
            return false;
        }

        var target = Path.Combine(modsDir, LoadOrderFile);
        var tmp = target + ".tmp";
        try
        {
            var lines = new List<string>
            {
                "# OoTMM mod load order - the TOP line loads over everything below it.",
                "# One archive filename per line; archives not listed here load first (lowest priority).",
                "# Managed by the launcher's Mods panels; hand-editing is fine.",
            };
            lines.AddRange(namesTopFirst);
            File.WriteAllLines(tmp, lines);
            File.Move(tmp, target, overwrite: true);
            return true;
        }
        catch (IOException)
        {
            try
            {
                File.Delete(tmp);
            }
            catch (IOException)
            {
            }

            return false;
        }
    }

    private static List<string> LoadLoadOrder(string modsDir)
    {
        var path = Path.Combine(modsDir, LoadOrderFile);
        if (!File.Exists(path))
        {
            return [];
        }

        try
        {
            return File.ReadAllLines(path)
                .Select(line => line.Trim())
                .Where(line => line.Length > 0 && !line.StartsWith('#'))
                .ToList();
        }
        catch (IOException)
        {
            // A half-written or locked manifest just means "no explicit order"
            // for this scan; the next 2s rescan picks it up once readable.
            return [];
        }
    }
}
