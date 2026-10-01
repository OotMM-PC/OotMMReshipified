using System.Security.Cryptography;
using System.Text.Json;

namespace OotMM.Core;

/// <summary>Durable small-file primitives shared by the session layer.</summary>
public static class DurableFile
{
    private const int TransientAttempts = 5;

    private static bool IsTransient(Exception ex) =>
        ex is UnauthorizedAccessException || (ex is IOException io && (io.HResult & 0xFFFF) is 32 or 33);

    public static bool WriteAtomic(string path, string contents)
    {
        var temporary = path + ".tmp";
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write))
                {
                    var bytes = System.Text.Encoding.UTF8.GetBytes(contents);
                    stream.Write(bytes);
                    stream.Flush(flushToDisk: true);
                }

                File.Move(temporary, path, overwrite: true);
                return true;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                try
                {
                    File.Delete(temporary);
                }
                catch (Exception cleanup) when (cleanup is IOException or UnauthorizedAccessException)
                {
                }

                if (attempt >= TransientAttempts || !IsTransient(ex))
                {
                    return false;
                }

                Thread.Sleep(20 << attempt);
            }
        }
    }

    public static bool TruncateDurably(string path)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                using var stream = new FileStream(path, FileMode.Create, FileAccess.Write);
                stream.Flush(flushToDisk: true);
                return true;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                if (attempt >= TransientAttempts || !IsTransient(ex))
                {
                    return false;
                }

                Thread.Sleep(20 << attempt);
            }
        }
    }

    public static bool WriteOffset(string path, long offset) =>
        WriteAtomic(path, offset + "\n");

    public static long? ReadOffset(string path)
    {
        try
        {
            var text = File.ReadAllText(path).Trim();
            return text.Length > 0
                   && long.TryParse(text, System.Globalization.NumberStyles.None, null, out var value)
                ? value
                : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>Removes a durable file via rename-away-then-delete so a crash
    /// can never leave the watched name half-present.</summary>
    public static bool Retire(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                return true;
            }

            var retired = path + ".retired";
            File.Move(path, retired, overwrite: true);
            try
            {
                File.Delete(retired);
            }
            catch (IOException)
            {
            }

            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}

/// <summary>A prepared play session: resolved identity, directory layout, and
/// bridge-file paths for one seed.</summary>
public sealed class Session
{
    public required string SeedPath { get; init; }
    public required string OotExe { get; init; }
    public required string MmExe { get; init; }
    public required Seed Seed { get; init; }
    // Settable live: the dev-mode toggle applies on the next game boot.
    public bool AllowDebugMenus { get; set; }
    public bool OpenFileSelect { get; set; }

    /// <summary>Set before PrepareSession; keys the v4 identity namespace.</summary>
    public bool V4Planned { get; init; }

    public string SessionDir { get; internal set; } = "";
    public string StatePath { get; internal set; } = "";
    public string DebugItemsPath { get; internal set; } = "";
    public string BootConfigPath { get; internal set; } = "";
    public string OotInbox { get; internal set; } = "";
    public string OotOutbox { get; internal set; } = "";
    public string MmInbox { get; internal set; } = "";
    public string MmOutbox { get; internal set; } = "";
    public string OotOutboxCursor { get; internal set; } = "";
    public string MmOutboxCursor { get; internal set; } = "";
    public string PendingTransitionPath { get; internal set; } = "";
    public string PresenceOut { get; internal set; } = "";
    public string PresenceIn { get; internal set; } = "";
    public string SaveId { get; internal set; } = "";
    public uint? LogicalSlot { get; internal set; }
    public bool LogicalSlotProvenNew { get; internal set; }
    public CrossGameTransition? RecoveredTransition { get; set; }
}

/// <summary>Session directory layout, seed/save identity tags, and boot-config
/// orchestration, ported from SessionPaths.cpp. Names must match the C++
/// launcher exactly so existing sessions resume instead of resetting.</summary>
public static class SessionPaths
{
    public const string V4NativeSaveSubdir = "ootmm-v4";

    /// <summary>Filesystem-safe per-seed tag: sanitized settingsHash (fallback
    /// seedId), max 32 chars.</summary>
    public static string SeedTag(Seed seed)
    {
        var source = seed.SettingsHash.Length > 0 ? seed.SettingsHash : seed.SeedId;
        var tag = new System.Text.StringBuilder();
        foreach (var c in source)
        {
            if (char.IsAsciiLetterOrDigit(c) || c is '-' or '_')
            {
                tag.Append(c);
            }

            if (tag.Length >= 32)
            {
                break;
            }
        }

        return tag.ToString();
    }

    /// <summary>Save-folder tag: SeedTag + _p&lt;player&gt; for multiworld,
    /// + _e&lt;epoch&gt; past epoch 1 — matches the ports' ActiveSeedTag.</summary>
    public static string SaveTag(Seed seed)
    {
        var tag = SeedTag(seed);
        if (seed.PlayerCount > 1)
        {
            tag += $"_p{seed.PlayerId}";
        }

        if (seed.SessionEpoch > 1)
        {
            tag += $"_e{seed.SessionEpoch}";
        }

        return tag;
    }

    public static string SessionBindingKey(Session session, string roomTag)
    {
        var key = $"{roomTag}_p{session.Seed.PlayerId}";
        return session.V4Planned ? key + "_v4" : key;
    }

    public static string SessionDirSuffix(Session session) =>
        session.V4Planned ? "_v4" : "_v3";

    public static string OotNativeSavesRoot(Session session)
    {
        var root = Path.Combine(Path.GetDirectoryName(session.OotExe)!, "Save");
        return session.V4Planned ? Path.Combine(root, V4NativeSaveSubdir) : root;
    }

    public static string MmNativeSavesRoot(Session session)
    {
        var root = Path.Combine(Path.GetDirectoryName(session.MmExe)!, "saves");
        return session.V4Planned ? Path.Combine(root, V4NativeSaveSubdir) : root;
    }

    public static bool IsValidSaveId(string value) =>
        value.Length == 32 && value.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f');

    public static string NewSaveId() =>
        Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(16));

    public static string? ReadSaveId(string path)
    {
        try
        {
            var value = File.ReadAllText(path).TrimEnd('\r', '\n');
            return IsValidSaveId(value) ? value : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public static string? GetOrCreateSlotSaveId(
        string sessionRoot, string bindingKey, uint slot, out string error)
    {
        error = "";
        var path = Path.Combine(sessionRoot, $"{bindingKey}_slot{slot}.save-id");
        if (ReadSaveId(path) is { } existing)
        {
            return existing;
        }

        var value = NewSaveId();
        if (!DurableFile.WriteAtomic(path, value + "\n"))
        {
            error = $"cannot persist save binding {path}";
            return null;
        }

        return value;
    }

    public static BootConfig MakeBootConfig(
        Session session, Game game, uint? bootEntrance, uint? ootAge = null)
    {
        var isOot = game == Game.Oot;
        return new BootConfig
        {
            SaveId = session.SaveId,
            SeedPath = session.SeedPath,
            StatePath = session.StatePath,
            InboxPath = isOot ? session.OotInbox : session.MmInbox,
            OutboxPath = isOot ? session.OotOutbox : session.MmOutbox,
            PresenceOutPath = session.PresenceOut,
            PresenceInPath = session.PresenceIn,
            MmSaveDir = MmNativeSavesRoot(session),
            NativeSaveSubdir = session.V4Planned ? V4NativeSaveSubdir : "",
            AllowDebugMenus = session.AllowDebugMenus,
            BootGame = game,
            BootEntrance = bootEntrance,
            OotAge = ootAge,
            LogicalSlot = session.LogicalSlot,
            MultiworldProtocol = session.V4Planned ? 4 : 3,
        };
    }

    /// <summary>Resolves the session directory and identity, creating or
    /// resuming durable state. Returns an error message, or null on success.</summary>
    public static string? PrepareSession(Session session)
    {
        try
        {
            return PrepareSessionInner(session);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return $"session preparation failed: {ex.Message}";
        }
    }

    public static bool TryPrepareSelectedSession(Session current, NativeSaveRequest request,
        ulong lastAcceptedRequestId, out Session? selected, out string error)
    {
        selected = null;
        error = "Native save selection does not match the active session.";
        if (request.Operation != NativeSaveOperation.Select || request.TargetSlot > 2
            || request.RequestId == 0 || request.RequestId > NativeSaveRequest.MaximumRequestId
            || !request.MatchesActiveSave(current.SaveId, current.LogicalSlot, lastAcceptedRequestId)
            || !IsValidSaveId(current.SaveId))
            return false;
        var target = new Session
        {
            SeedPath = current.SeedPath,
            OotExe = current.OotExe,
            MmExe = current.MmExe,
            Seed = current.Seed,
            AllowDebugMenus = current.AllowDebugMenus,
            V4Planned = current.V4Planned,
            LogicalSlot = request.TargetSlot,
        };
        try
        {
            if (PrepareSessionInner(target, request.TargetSlot) is { } preparationError)
            {
                error = preparationError;
                return false;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            error = $"Save selection failed: {ex.Message}";
            return false;
        }
        selected = target;
        error = "";
        return true;
    }

    private static string? PrepareSessionInner(Session session, uint? selectedSlot = null)
    {
        var sessionRoot = Path.GetFullPath(
            Path.Combine(Path.GetDirectoryName(session.SeedPath)!, ".ootmm-session"));
        Directory.CreateDirectory(sessionRoot);

        var roomTag = SeedTag(session.Seed);
        if (roomTag.Length == 0)
        {
            roomTag = "default";
        }

        var bindingKey = SessionBindingKey(session, roomTag);
        if (!NativeSaveCopy.RecoverBindings(session, sessionRoot, bindingKey, out var copyRecoveryError))
            return $"Save copy recovery must finish before starting: {copyRecoveryError}";
        if (!session.V4Planned)
        {
            MigrateLegacySaveBinding(sessionRoot, bindingKey);
        }

        uint? recordedSelection = null;
        if (selectedSlot is null && session.LogicalSlot is null)
        {
            var selectionPath = Path.Combine(sessionRoot, bindingKey + ".selected-save.json");
            if (File.Exists(selectionPath))
            {
                try
                {
                    using var document = JsonDocument.Parse(File.ReadAllText(selectionPath));
                    var root = document.RootElement;
                    if (root.ValueKind != JsonValueKind.Object
                        || root.EnumerateObject().Count() != 3
                        || !root.TryGetProperty("version", out var version) || version.ValueKind != JsonValueKind.Number
                        || !version.TryGetUInt32(out var versionNumber) || versionNumber != 1
                        || !root.TryGetProperty("slot", out var slotValue) || slotValue.ValueKind != JsonValueKind.Number
                        || !slotValue.TryGetUInt32(out var slot) || slot > 2
                        || !root.TryGetProperty("saveId", out var identity) || identity.ValueKind != JsonValueKind.String
                        || ReadSaveId(Path.Combine(sessionRoot, $"{bindingKey}_slot{slot}.save-id")) is not { } boundId
                        || identity.GetString() != boundId)
                        return "Selected save record is invalid or does not match its slot identity.";
                    recordedSelection = slot;
                }
                catch (JsonException)
                {
                    return "Selected save record is malformed.";
                }
            }
        }
        var boundSlot = session.LogicalSlot ?? recordedSelection
                        ?? DiscoverBoundLogicalSlot(sessionRoot, bindingKey, SessionDirSuffix(session));
        session.LogicalSlot = boundSlot;
        var saveId = GetOrCreateSlotSaveId(sessionRoot, bindingKey, boundSlot, out var bindingError);
        if (saveId is null)
        {
            return bindingError;
        }

        session.SaveId = saveId;
        var seedKey = $"{bindingKey}_{saveId}{SessionDirSuffix(session)}";
        var sessionDir = Path.Combine(sessionRoot, seedKey);
        Directory.CreateDirectory(sessionDir);

        session.SessionDir = sessionDir;
        session.StatePath = Path.Combine(sessionDir, "state.json");
        session.DebugItemsPath = Path.Combine(sessionDir, "debug-items.json");
        session.BootConfigPath = Path.Combine(sessionDir, "boot.json");
        session.OotInbox = Path.Combine(sessionDir, "oot.inbox.jsonl");
        session.OotOutbox = Path.Combine(sessionDir, "oot.outbox.jsonl");
        session.MmInbox = Path.Combine(sessionDir, "mm.inbox.jsonl");
        session.MmOutbox = Path.Combine(sessionDir, "mm.outbox.jsonl");
        session.OotOutboxCursor = Path.Combine(sessionDir, "oot.outbox.cursor");
        session.MmOutboxCursor = Path.Combine(sessionDir, "mm.outbox.cursor");
        session.PendingTransitionPath = Path.Combine(sessionDir, "launcher.pending-transition.json");
        session.PresenceOut = Path.Combine(sessionDir, "presence.out.json");
        session.PresenceIn = Path.Combine(sessionDir, "presence.in.json");

        // Marker match => RESUME; mismatch/missing => fresh seed in this dir.
        var markerPath = Path.Combine(sessionDir, "session.id");
        var resume = false;
        if (File.Exists(markerPath))
        {
            var existing = File.ReadAllText(markerPath);
            resume = SessionOwnership.ValidateMarker(existing, session.Seed, seedKey);
            if (!resume && existing.TrimEnd('\r', '\n') == seedKey
                && DurableFile.WriteAtomic(markerPath, SessionOwnership.BuildMarker(session.Seed, seedKey)))
            {
                resume = true; // legacy tag-only marker upgraded in place
            }
        }

        if (!resume)
        {
            var sessionEvidence = Directory.EnumerateFileSystemEntries(sessionDir).Any();
            var saveTag = SaveTag(session.Seed);
            var nativeEvidence = new[]
            {
                Path.Combine(OotNativeSavesRoot(session), "ootmm-" + saveTag),
                Path.Combine(MmNativeSavesRoot(session), "ootmm-" + saveTag),
            }.Any(dir => Directory.Exists(dir) && Directory.EnumerateFileSystemEntries(dir).Any());
            resume = sessionEvidence || nativeEvidence;
        }

        if (resume && File.Exists(session.PendingTransitionPath))
        {
            var line = File.ReadAllText(session.PendingTransitionPath);
            var transition = AnchorPackets.ParseCrossGameTransition(line);
            if (transition is null || transition.FromGame == transition.ToGame
                || !AnchorPackets.MatchesSeedIdentity(line, session.Seed))
            {
                return "durable pending cross-game transition is malformed or belongs to another seed";
            }

            session.RecoveredTransition = transition;
        }

        if (EnsureOutboxCursor(session.OotOutbox, session.OotOutboxCursor) is { } ootError)
        {
            return ootError;
        }

        if (EnsureOutboxCursor(session.MmOutbox, session.MmOutboxCursor) is { } mmError)
        {
            return mmError;
        }

        if (!resume)
        {
            if (!DurableFile.WriteOffset(session.OotOutboxCursor, 0)
                || !DurableFile.TruncateDurably(session.OotOutbox)
                || !DurableFile.WriteOffset(session.MmOutboxCursor, 0)
                || !DurableFile.TruncateDurably(session.MmOutbox))
            {
                return "cannot initialize durable game outboxes";
            }

            File.WriteAllText(session.OotInbox, "");
            File.WriteAllText(session.MmInbox, "");
            File.Delete(session.StatePath);
            File.Delete(session.DebugItemsPath);
            foreach (var stale in new[]
                     {
                         "multiworld.pending.jsonl", "multiworld.seq",
                         "multiworld.acked-checks.jsonl", "multiworld.pending.quarantine.jsonl",
                     })
            {
                File.Delete(Path.Combine(sessionDir, stale));
            }

            if (!DurableFile.WriteAtomic(markerPath, SessionOwnership.BuildMarker(session.Seed, seedKey)))
            {
                return $"cannot durably initialize session ownership marker {markerPath}";
            }
        }

        if ((selectedSlot ?? recordedSelection) is { } explicitSlot)
        {
            if (File.Exists(session.StatePath))
            {
                var stateText = File.ReadAllText(session.StatePath);
                var recordedSlot = SessionOwnership.ParseRuntimeLogicalSlot(stateText, out var stateError);
                if ((stateError.Length > 0 && !IsLegacyCheckOnlyState(stateText))
                    || recordedSlot is { } recorded && recorded != explicitSlot)
                    return "Selected save state does not match its slot binding.";
            }
            session.LogicalSlot = explicitSlot;
            session.LogicalSlotProvenNew = false;
        }
        else if (ResolveSessionLogicalSlot(session) is { } slotError)
        {
            // Keep the local slot binding; the warning surfaces via the caller's log.
            session.LogicalSlot = boundSlot;
        }

        return null;
    }

    private static bool IsLegacyCheckOnlyState(string text)
    {
        try
        {
            using var document = JsonDocument.Parse(text);
            var root = document.RootElement;
            return root.ValueKind == JsonValueKind.Object && root.EnumerateObject().Count() == 1
                && root.TryGetProperty("completedChecks", out var checks) && checks.ValueKind == JsonValueKind.Array
                && checks.EnumerateArray().All(check => check.ValueKind == JsonValueKind.String);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    public static bool RecordSelectedSession(Session session, out string error)
    {
        error = "Cannot record an unresolved or mismatched save selection.";
        if (session.LogicalSlot is not { } slot || slot > 2 || !IsValidSaveId(session.SaveId))
            return false;
        var root = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(session.SeedPath)!, ".ootmm-session"));
        var tag = SeedTag(session.Seed);
        var key = SessionBindingKey(session, tag.Length == 0 ? "default" : tag);
        if (ReadSaveId(Path.Combine(root, $"{key}_slot{slot}.save-id")) != session.SaveId)
            return false;
        var path = Path.Combine(root, key + ".selected-save.json");
        if (!DurableFile.WriteAtomic(path, JsonSerializer.Serialize(new { version = 1, slot, saveId = session.SaveId })))
        {
            error = "Could not persist the selected save slot.";
            return false;
        }
        error = "";
        return true;
    }

    private static string? EnsureOutboxCursor(string outbox, string cursor)
    {
        var outboxSize = File.Exists(outbox) ? new FileInfo(outbox).Length : 0;
        if (File.Exists(cursor))
        {
            var value = DurableFile.ReadOffset(cursor);
            if ((value is null || value > outboxSize) && !DurableFile.WriteOffset(cursor, 0))
            {
                return $"cannot recover durable outbox cursor {cursor}";
            }

            return null;
        }

        return DurableFile.WriteOffset(cursor, 0) ? null : $"cannot initialize durable outbox cursor {cursor}";
    }

    private static void MigrateLegacySaveBinding(string sessionRoot, string bindingKey)
    {
        for (var slot = 0u; slot < 3; slot++)
        {
            if (ReadSaveId(Path.Combine(sessionRoot, $"{bindingKey}_slot{slot}.save-id")) is not null)
            {
                return;
            }
        }

        var legacy = ReadSaveId(Path.Combine(sessionRoot, bindingKey + ".save-id"));
        if (legacy is null)
        {
            return;
        }

        var slotIndex = 0u;
        var statePath = Path.Combine(sessionRoot, $"{bindingKey}_{legacy}_v3", "state.json");
        if (File.Exists(statePath)
            && SessionOwnership.ParseRuntimeLogicalSlot(File.ReadAllText(statePath), out _) is { } active)
        {
            slotIndex = active;
        }

        DurableFile.WriteAtomic(
            Path.Combine(sessionRoot, $"{bindingKey}_slot{slotIndex}.save-id"), legacy + "\n");
    }

    private static uint DiscoverBoundLogicalSlot(string sessionRoot, string bindingKey, string dirSuffix)
    {
        uint? selected = null;
        var selectedTime = DateTime.MinValue;
        for (var slot = 0u; slot < 3; slot++)
        {
            var saveId = ReadSaveId(Path.Combine(sessionRoot, $"{bindingKey}_slot{slot}.save-id"));
            if (saveId is null)
            {
                continue;
            }

            var statePath = Path.Combine(sessionRoot, $"{bindingKey}_{saveId}{dirSuffix}", "state.json");
            if (!File.Exists(statePath))
            {
                continue;
            }

            var active = SessionOwnership.ParseRuntimeLogicalSlot(File.ReadAllText(statePath), out _);
            var changed = File.GetLastWriteTimeUtc(statePath);
            if (active == slot && (selected is null || changed > selectedTime))
            {
                selected = slot;
                selectedTime = changed;
            }
        }

        return selected ?? 0;
    }

    private static string? ResolveSessionLogicalSlot(Session session)
    {
        string? ReadOptional(string path) => File.Exists(path) ? File.ReadAllText(path) : null;

        var slotStates = new string?[3];
        for (var slot = 0; slot < 3; slot++)
        {
            slotStates[slot] = ReadOptional($"{session.StatePath}.slot{slot}");
        }

        var mmSaveDir = Path.Combine(MmNativeSavesRoot(session), "ootmm-" + SaveTag(session.Seed));
        var evidence = new LogicalSlotEvidence(
            SessionOwnershipVerified: true,
            BaseState: ReadOptional(session.StatePath),
            SlotStates: slotStates,
            MmNativeSaveExists: Directory.Exists(mmSaveDir)
                                && Directory.EnumerateFileSystemEntries(mmSaveDir).Any());

        var resolution = SessionOwnership.ResolveLogicalSlotIdentity(evidence);
        if (!resolution.IsSafe)
        {
            return $"cannot resolve MM logical save-slot identity: {resolution.Error}";
        }

        session.LogicalSlot = resolution.LogicalSlot;
        session.LogicalSlotProvenNew = resolution.ProvenNew;
        return null;
    }
}
