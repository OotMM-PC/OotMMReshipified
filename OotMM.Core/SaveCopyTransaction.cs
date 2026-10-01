using System.Text.Json;

namespace OotMM.Core;

public sealed class SaveCopyTransaction
{
    private readonly string _journal;
    private readonly Dictionary<string, string> _destinations;
    private sealed record Journal(int Version, bool Committed, Dictionary<string, byte[]?> Before);

    public SaveCopyTransaction(string journal, IReadOnlyDictionary<string, string> destinations)
    {
        _journal = Path.GetFullPath(journal);
        _destinations = destinations.ToDictionary(p => p.Key, p => Path.GetFullPath(p.Value), StringComparer.Ordinal);
        var paths = _destinations.Values.ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (_destinations.Count == 0 || paths.Count != _destinations.Count || paths.Contains(_journal)
            || paths.Contains(_journal + ".tmp") || _destinations.Keys.Any(string.IsNullOrWhiteSpace))
            throw new ArgumentException("Copy destinations must be distinct from each other and the journal.");
    }

    public bool Execute(IReadOnlyDictionary<string, byte[]?> replacements, out string error)
    {
        error = "Copy replacement set does not match its destinations.";
        if (replacements.Count != _destinations.Count || replacements.Keys.Any(k => !_destinations.ContainsKey(k)))
            return false;
        if (!Recover(out error))
            return false;
        try
        {
            var before = _destinations.ToDictionary(p => p.Key,
                p => File.Exists(p.Value) ? File.ReadAllBytes(p.Value) : null, StringComparer.Ordinal);
            var journal = new Journal(1, false, before);
            if (!WriteJournal(journal))
            {
                error = "Could not preserve destination saves before copying.";
                return false;
            }
            foreach (var (key, bytes) in replacements)
                Replace(_destinations[key], bytes);
            if (!WriteJournal(journal with { Committed = true }))
                throw new IOException("Could not commit the copied save set.");
            DurableFile.Retire(_journal);
            error = "";
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            var recovered = Recover(out var recoveryError);
            error = recovered ? $"Save copy failed and destination saves were restored: {ex.Message}"
                : $"Save copy recovery is pending: {recoveryError}";
            return false;
        }
    }

    public bool Recover(out string error)
    {
        error = "";
        try
        {
            if (!File.Exists(_journal))
                return true;
            var text = File.ReadAllText(_journal);
            using var document = JsonDocument.Parse(text);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || root.EnumerateObject().Count() != 3
                || !root.TryGetProperty("Version", out var version) || version.ValueKind != JsonValueKind.Number
                || !version.TryGetInt32(out var number) || number != 1
                || !root.TryGetProperty("Committed", out var committed)
                || committed.ValueKind is not (JsonValueKind.True or JsonValueKind.False)
                || !root.TryGetProperty("Before", out var entries) || entries.ValueKind != JsonValueKind.Object
                || entries.EnumerateObject().Count() != _destinations.Count
                || entries.EnumerateObject().Select(p => p.Name).Distinct(StringComparer.Ordinal).Count() != _destinations.Count
                || entries.EnumerateObject().Any(p => !_destinations.ContainsKey(p.Name)))
                throw new JsonException("Copy journal does not match the expected destination set.");
            var journal = JsonSerializer.Deserialize<Journal>(text)!;
            if (!journal.Committed)
            {
                foreach (var (key, original) in journal.Before)
                {
                    var path = _destinations[key];
                    if (original is null && !File.Exists(path))
                        continue;
                    if (original is not null && File.Exists(path) && File.ReadAllBytes(path).AsSpan().SequenceEqual(original))
                        continue;
                    Replace(path, original);
                }
            }
            if (!DurableFile.Retire(_journal))
                throw new IOException("Could not retire the recovered copy journal.");
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            error = ex.Message;
            return false;
        }
    }

    private bool WriteJournal(Journal journal) => DurableFile.WriteAtomic(_journal, JsonSerializer.Serialize(journal));

    private static void Replace(string path, byte[]? bytes)
    {
        if (bytes is null)
        {
            File.Delete(path);
            return;
        }
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + ".copy-" + Guid.NewGuid().ToString("N");
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                stream.Write(bytes);
                stream.Flush(flushToDisk: true);
            }
            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            try { File.Delete(temporary); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}
