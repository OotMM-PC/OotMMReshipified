using System.Text.Json;
using System.Text.Json.Nodes;

namespace OotMM.Core;

public static class NativeSaveCopyPayload
{
    public static bool TryCreate(Game game, string source, string sourceSaveId, uint sourceSlot,
        string targetSaveId, uint targetSlot, out string copied, out string error)
    {
        copied = "";
        error = "Invalid native save copy identity or payload.";
        if (!SessionPaths.IsValidSaveId(sourceSaveId) || !SessionPaths.IsValidSaveId(targetSaveId)
            || sourceSaveId == targetSaveId || sourceSlot > 2 || targetSlot > 2 || sourceSlot == targetSlot
            || source.Length > 8 * 1024 * 1024 || game is not (Game.Oot or Game.Mm))
            return false;
        try
        {
            using var document = JsonDocument.Parse(source);
            if (document.RootElement.ValueKind != JsonValueKind.Object || !HasUniqueFields(document.RootElement))
                return false;
            var root = JsonNode.Parse(source)!.AsObject();
            var sections = new List<JsonObject>();
            if (game == Game.Oot)
            {
                if (root["sections"] is not JsonObject nativeSections
                    || nativeSections["base"] is not JsonObject baseSection
                    || baseSection["data"] is not JsonObject)
                    return false;
                sections.Add(root);
            }
            else
            {
                foreach (var name in new[] { "newCycleSave", "owlSave" })
                {
                    if (!root.ContainsKey(name))
                        continue;
                    if (root[name] is not JsonObject section || section["save"] is not JsonObject)
                        return false;
                    sections.Add(section);
                }
                if (sections.Count == 0)
                    return false;
            }
            foreach (var section in sections)
            {
                if (!section.ContainsKey("ootmmSharedResources"))
                    continue;
                if (section["ootmmSharedResources"] is not JsonObject shared
                    || !SharedResourceSnapshot.TryParse(shared.ToJsonString(), sourceSaveId, sourceSlot,
                        out _, out error))
                    return false;
                shared["saveId"] = targetSaveId;
                shared["slot"] = targetSlot;
                if (!SharedResourceSnapshot.TryParse(shared.ToJsonString(), targetSaveId, targetSlot,
                        out _, out error))
                    return false;
            }
            copied = root.ToJsonString();
            error = "";
            return true;
        }
        catch (JsonException ex)
        {
            error = ex.Message;
            return false;
        }
    }

    internal static bool HasUniqueFields(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in element.EnumerateObject())
                if (!names.Add(property.Name) || !HasUniqueFields(property.Value))
                    return false;
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
                if (!HasUniqueFields(item))
                    return false;
        }
        return true;
    }
}
