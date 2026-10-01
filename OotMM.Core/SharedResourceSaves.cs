using System.Text.Json;

namespace OotMM.Core;

public static class SharedResourceSaves
{
    public static bool TryRead(Session session, out SharedResourceSnapshot? latest, out string error,
        MmSaveSource? selectedMm = null, bool mmSourceSelected = false)
    {
        latest = null;
        error = "";
        if (!session.Seed.GetBoolSetting("sharedBombchu"))
            return true;
        if (session.LogicalSlot is not { } slot)
        {
            error = "Cannot restore shared resources without a resolved logical save slot.";
            return false;
        }
        var tag = "ootmm-" + SessionPaths.SaveTag(session.Seed);
        var mmPath = Path.Combine(SessionPaths.MmNativeSavesRoot(session), tag, $"file{slot + 1}.json");
        var mmSource = selectedMm;
        if (!mmSourceSelected && mmSource is null && !MmSaveSource.TrySelect(mmPath, session.SaveId, slot, out mmSource, out error))
            return false;
        return TryRead(Path.Combine(SessionPaths.OotNativeSavesRoot(session), tag, $"file{slot + 1}.sav"),
            mmPath, session.SaveId, slot, out latest, out error, mmSource?.Contents ?? "{}");
    }

    public static bool TryRead(string ootPath, string mmPath, string saveId, uint slot,
        out SharedResourceSnapshot? latest, out string error, string? mmContents = null)
    {
        latest = null;
        error = "";
        if (slot > 2 || string.IsNullOrEmpty(saveId))
        {
            error = "Shared-resource recovery requires a resolved save and slot.";
            return false;
        }
        var candidates = new List<SharedResourceSnapshot>();
        foreach (var (path, game) in new[] { (ootPath, Game.Oot), (mmPath, Game.Mm) })
        {
            try
            {
                using Stream stream = game == Game.Mm && mmContents is not null
                    ? new MemoryStream(System.Text.Encoding.UTF8.GetBytes(mmContents)) : File.OpenRead(path);
                using var document = JsonDocument.Parse(stream);
                var root = document.RootElement;
                if (root.ValueKind != JsonValueKind.Object)
                {
                    error = $"Native save is not an object: {path}";
                    return false;
                }
                var sections = new List<JsonElement>();
                if (game == Game.Oot)
                    sections.Add(root);
                else
                {
                    foreach (var name in new[] { "owlSave", "newCycleSave" })
                    {
                        var entries = root.EnumerateObject().Where(p => p.Name == name).ToArray();
                        if (entries.Length > 1)
                        {
                            error = $"Duplicate native save section: {path}";
                            return false;
                        }
                        if (entries.Length == 1)
                            sections.Add(entries[0].Value);
                    }
                }
                foreach (var section in sections)
                {
                    if (section.ValueKind != JsonValueKind.Object)
                    {
                        error = $"Invalid native save section: {path}";
                        return false;
                    }
                    var matches = section.EnumerateObject().Where(p => p.Name == "ootmmSharedResources").ToArray();
                    if (matches.Length == 0)
                        continue;
                    if (matches.Length != 1 || !SharedResourceSnapshot.TryParse(matches[0].Value.GetRawText(),
                            saveId, slot, out var snapshot, out error))
                    {
                        error = $"Invalid shared resources in {path}: {error}";
                        return false;
                    }
                    candidates.Add(snapshot!);
                }
            }
            catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
            {
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
            {
                error = $"Cannot read shared resources from {path}: {ex.Message}";
                return false;
            }
        }
        return SharedResourceSnapshot.TrySelect(candidates, saveId, slot, out latest, out error);
    }
}
