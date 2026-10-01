using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace OotMM.Core;

public sealed record MmSaveSource(string Path, string Sha256, string Contents, bool FromBackup,
    string? DamagedPrimarySha256, SharedResourceSnapshot? SharedResources, string? DamagedPrimaryHex)
{
    public static bool TrySelect(string primaryPath, string saveId, uint slot,
        out MmSaveSource? selected, out string error)
    {
        selected = null;
        error = "Invalid MM save recovery identity.";
        if (!SessionPaths.IsValidSaveId(saveId) || slot > 2)
            return false;
        var backupPath = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(primaryPath)!,
            System.IO.Path.GetFileNameWithoutExtension(primaryPath) + "backup.json");
        string? damagedHash = null;
        string? damagedHex = null;
        for (var index = 0; index < 2; index++)
        {
            var path = index == 0 ? primaryPath : backupPath;
            byte[] bytes;
            try
            {
                using var stream = File.OpenRead(path);
                if (stream.Length > 8 * 1024 * 1024)
                {
                    error = $"MM save exceeds the supported size: {path}";
                    return false;
                }
                bytes = new byte[checked((int)stream.Length)];
                stream.ReadExactly(bytes);
                if (stream.ReadByte() != -1)
                {
                    error = $"MM save changed while being read: {path}";
                    return false;
                }
            }
            catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
            {
                if (index == 0)
                    continue;
                error = damagedHash is null ? "" : "The damaged MM primary has no backup to recover.";
                return damagedHash is null;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                error = $"Cannot read MM save {path}: {ex.Message}";
                return false;
            }
            var hash = Convert.ToHexStringLower(SHA256.HashData(bytes));
            string contents;
            JsonDocument document;
            try
            {
                contents = new UTF8Encoding(false, true).GetString(bytes);
                document = JsonDocument.Parse(contents);
            }
            catch (Exception ex) when (ex is JsonException or DecoderFallbackException)
            {
                if (index == 0)
                {
                    damagedHash = hash;
                    damagedHex = Convert.ToHexStringLower(bytes);
                    continue;
                }
                error = $"MM backup is unreadable: {ex.Message}";
                return false;
            }
            using (document)
            {
                var root = document.RootElement;
                if (root.ValueKind != JsonValueKind.Object || !NativeSaveCopyPayload.HasUniqueFields(root))
                {
                    error = $"MM save has an invalid or ambiguous structure: {path}";
                    return false;
                }
                var count = 0;
                var snapshots = new List<SharedResourceSnapshot>();
                foreach (var name in new[] { "newCycleSave", "owlSave" })
                {
                    if (!root.TryGetProperty(name, out var section))
                        continue;
                    count++;
                    if (section.ValueKind != JsonValueKind.Object
                        || !section.TryGetProperty("save", out var save) || save.ValueKind != JsonValueKind.Object)
                    {
                        error = $"MM save has an invalid {name} envelope: {path}";
                        return false;
                    }
                    if (section.TryGetProperty("ootmmSharedResources", out var shared))
                    {
                        if (!SharedResourceSnapshot.TryParse(shared.GetRawText(), saveId, slot,
                                out var snapshot, out error))
                            return false;
                        snapshots.Add(snapshot!);
                    }
                }
                if (count == 0)
                {
                    error = $"MM save has no native save envelope: {path}";
                    return false;
                }
                if (!SharedResourceSnapshot.TrySelect(snapshots, saveId, slot, out var latest, out error))
                    return false;
                selected = new MmSaveSource(path, hash, contents, index == 1, damagedHash, latest, damagedHex);
                error = "";
                return true;
            }
        }
        error = "No MM recovery source could be selected.";
        return false;
    }
}
