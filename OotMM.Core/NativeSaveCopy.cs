using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace OotMM.Core;

public static class NativeSaveCopy
{
    public static bool TryExecute(Session source, Session target, out string error)
    {
        error = "Save copy requires distinct slots and identities in the same seed and native save directories.";
        if (source.LogicalSlot is not { } sourceSlot || target.LogicalSlot is not { } targetSlot
            || sourceSlot > 2 || targetSlot > 2 || sourceSlot == targetSlot || source.SaveId == target.SaveId
            || !SessionPaths.IsValidSaveId(source.SaveId) || !SessionPaths.IsValidSaveId(target.SaveId)
            || source.V4Planned != target.V4Planned || source.Seed.PlayerId != target.Seed.PlayerId
            || SessionOwnership.BuildMarker(source.Seed, "copy") != SessionOwnership.BuildMarker(target.Seed, "copy")
            || !SamePath(SessionPaths.OotNativeSavesRoot(source), SessionPaths.OotNativeSavesRoot(target))
            || !SamePath(SessionPaths.MmNativeSavesRoot(source), SessionPaths.MmNativeSavesRoot(target))
            || !SamePath(Path.GetDirectoryName(source.SessionDir)!, Path.GetDirectoryName(target.SessionDir)!))
            return false;
        try
        {
            if (File.Exists(source.PendingTransitionPath))
            {
                error = "Finish the source save's pending game transition before copying it.";
                return false;
            }
            var from = Destinations(source, source.SessionDir, sourceSlot);
            var to = Destinations(target, target.SessionDir, targetSlot);
            var replacements = to.Keys.ToDictionary(k => k, _ => (byte[]?)null, StringComparer.Ordinal);
            var foundNative = false;
            foreach (var (key, game) in new[] { ("oot", Game.Oot), ("mm", Game.Mm) })
            {
                if (!File.Exists(from[key]))
                    continue;
                foundNative = true;
                if (!NativeSaveCopyPayload.TryCreate(game, File.ReadAllText(from[key]), source.SaveId, sourceSlot,
                        target.SaveId, targetSlot, out var payload, out error))
                    return false;
                replacements[key] = Encoding.UTF8.GetBytes(payload);
            }
            if (!foundNative)
            {
                error = "The source slot has no native save to copy.";
                return false;
            }
            replacements["mmBackup"] = replacements["mm"];
            var stateText = File.ReadAllText(source.StatePath);
            using var stateDocument = JsonDocument.Parse(stateText);
            if (!NativeSaveCopyPayload.HasUniqueFields(stateDocument.RootElement))
            {
                error = "Source check state contains duplicate fields.";
                return false;
            }
            if (JsonNode.Parse(stateText) is not JsonObject state
                || state["completedChecks"] is not JsonArray checks
                || checks.Any(c => c is not JsonValue value || !value.TryGetValue<string>(out _)))
            {
                error = "Source check state is missing or malformed.";
                return false;
            }
            if (state["sessionValues"] is not JsonObject values)
                state["sessionValues"] = values = new JsonObject();
            values["ootmm.activeSlot"] = targetSlot.ToString(System.Globalization.CultureInfo.InvariantCulture);
            replacements["state"] = Encoding.UTF8.GetBytes(state.ToJsonString());
            var inventory = new SessionInventory(source.Seed, source.DebugItemsPath);
            if (!inventory.IsValid)
            {
                error = inventory.LastError;
                return false;
            }
            foreach (var key in new[] { "debug", "ferry", "foreign" })
                replacements[key] = File.Exists(from[key]) ? File.ReadAllBytes(from[key]) : null;
            foreach (var key in new[] { "ootInbox", "mmInbox", "ootOutbox", "mmOutbox" })
                replacements[key] = [];
            replacements["ootCursor"] = replacements["mmCursor"] = Encoding.UTF8.GetBytes("0\n");
            return new SaveCopyTransaction(Path.Combine(target.SessionDir, "save-copy.pending.json"), to)
                .Execute(replacements, out error);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            error = $"Could not prepare save copy: {ex.Message}";
            return false;
        }
    }

    public static bool RecoverBindings(Session session, string sessionRoot, string bindingKey, out string error)
    {
        error = "";
        for (var slot = 0u; slot < 3; slot++)
        {
            var saveId = SessionPaths.ReadSaveId(Path.Combine(sessionRoot, $"{bindingKey}_slot{slot}.save-id"));
            if (saveId is null)
                continue;
            var directory = Path.Combine(sessionRoot, $"{bindingKey}_{saveId}{SessionPaths.SessionDirSuffix(session)}");
            if (!new SaveCopyTransaction(Path.Combine(directory, "save-copy.pending.json"),
                    Destinations(session, directory, slot)).Recover(out error))
                return false;
        }
        return true;
    }

    private static bool SamePath(string left, string right) =>
        string.Equals(Path.GetFullPath(left), Path.GetFullPath(right), StringComparison.OrdinalIgnoreCase);

    private static Dictionary<string, string> Destinations(Session session, string directory, uint slot)
    {
        var tag = "ootmm-" + SessionPaths.SaveTag(session.Seed);
        return new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["oot"] = Path.Combine(SessionPaths.OotNativeSavesRoot(session), tag, $"file{slot + 1}.sav"),
            ["mm"] = Path.Combine(SessionPaths.MmNativeSavesRoot(session), tag, $"file{slot + 1}.json"),
            ["mmBackup"] = Path.Combine(SessionPaths.MmNativeSavesRoot(session), tag, $"file{slot + 1}backup.json"),
            ["state"] = Path.Combine(directory, "state.json"),
            ["debug"] = Path.Combine(directory, "debug-items.json"),
            ["ferry"] = Path.Combine(directory, "log.v4.jsonl"),
            ["foreign"] = Path.Combine(directory, "foreignplacements.v4.json"),
            ["ootInbox"] = Path.Combine(directory, "oot.inbox.jsonl"),
            ["mmInbox"] = Path.Combine(directory, "mm.inbox.jsonl"),
            ["ootOutbox"] = Path.Combine(directory, "oot.outbox.jsonl"),
            ["mmOutbox"] = Path.Combine(directory, "mm.outbox.jsonl"),
            ["ootCursor"] = Path.Combine(directory, "oot.outbox.cursor"),
            ["mmCursor"] = Path.Combine(directory, "mm.outbox.cursor"),
            ["transition"] = Path.Combine(directory, "launcher.pending-transition.json"),
        };
    }
}
