using System.Text;
using System.Text.Json;

namespace OotMM.Core;

/// <summary>Evidence for resolving which logical save slot a session owns.</summary>
public sealed record LogicalSlotEvidence(
    bool SessionOwnershipVerified,
    string? BaseState,
    IReadOnlyList<string?> SlotStates,
    bool MmNativeSaveExists);

public sealed record LogicalSlotResolution(uint? LogicalSlot, bool ProvenNew, string Error)
{
    public bool IsSafe => Error.Length == 0;
}

/// <summary>Session ownership marker (byte-exact validation) and logical-slot
/// identity resolution, ported from BootConfig.cpp.</summary>
public static class SessionOwnership
{
    public static string BuildMarker(Seed seed, string sessionKey)
    {
        return new StringBuilder()
            .Append("{\"format\":\"ootmm.session-owner.v2\"")
            .Append(",\"sessionKey\":").Append(JsonText.EscapeString(sessionKey))
            .Append(",\"seedId\":").Append(JsonText.EscapeString(seed.SeedId))
            .Append(",\"settingsHash\":").Append(JsonText.EscapeString(seed.SettingsHash))
            .Append(",\"teamId\":").Append(JsonText.EscapeString(seed.TeamId))
            .Append(",\"playerId\":").Append(seed.PlayerId)
            .Append(",\"sessionEpoch\":").Append(JsonText.EscapeString(seed.SessionEpoch.ToString()))
            .Append(",\"manifestHash\":").Append(JsonText.EscapeString(Identity.SeedManifestHash(seed)))
            .Append('}')
            .ToString();
    }

    /// <summary>Byte-exact compare: legacy or non-canonical markers must fail
    /// and go through the explicit copy-safe migration instead.</summary>
    public static bool ValidateMarker(string text, Seed seed, string sessionKey) =>
        text == BuildMarker(seed, sessionKey);

    /// <summary>Reads sessionValues["ootmm.activeSlot"] ("0".."2") from a
    /// runtime journal; null with a reason otherwise.</summary>
    public static uint? ParseRuntimeLogicalSlot(string stateText, out string error)
    {
        error = "";
        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(stateText);
        }
        catch (JsonException ex)
        {
            error = $"cannot parse runtime journal: {ex.Message}";
            return null;
        }

        using (doc)
        {
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
            {
                error = "runtime journal is not a JSON object";
                return null;
            }

            if (!doc.RootElement.TryGetProperty("sessionValues", out var sessionValues)
                || sessionValues.ValueKind != JsonValueKind.Object)
            {
                error = "runtime journal has no sessionValues object";
                return null;
            }

            if (!sessionValues.TryGetProperty("ootmm.activeSlot", out var activeSlot)
                || activeSlot.ValueKind != JsonValueKind.String)
            {
                error = "runtime journal has no exact ootmm.activeSlot identity";
                return null;
            }

            var value = activeSlot.GetString()!;
            if (value.Length != 1 || value[0] < '0' || value[0] > '2')
            {
                error = "runtime journal has an invalid ootmm.activeSlot identity";
                return null;
            }

            return (uint)(value[0] - '0');
        }
    }

    public static LogicalSlotResolution ResolveLogicalSlotIdentity(LogicalSlotEvidence evidence)
    {
        if (!evidence.SessionOwnershipVerified)
        {
            return new LogicalSlotResolution(null, false,
                "session ownership/epoch marker was not verified before slot resolution");
        }

        if (evidence.BaseState is { } baseState)
        {
            var baseSlot = ParseRuntimeLogicalSlot(baseState, out var error);
            if (baseSlot is null)
            {
                return new LogicalSlotResolution(null, false, $"base state: {error}");
            }

            if (evidence.SlotStates[(int)baseSlot.Value] is { } sidecarState)
            {
                var sidecarSlot = ParseRuntimeLogicalSlot(sidecarState, out var sidecarError);
                if (sidecarSlot is null)
                {
                    return new LogicalSlotResolution(null, false, $"selected slot sidecar: {sidecarError}");
                }

                if (sidecarSlot != baseSlot)
                {
                    return new LogicalSlotResolution(null, false,
                        $"selected slot sidecar identifies slot {sidecarSlot} instead of base slot {baseSlot}");
                }
            }

            return new LogicalSlotResolution(baseSlot, false, "");
        }

        uint? recovered = null;
        for (var slot = 0u; slot < evidence.SlotStates.Count; slot++)
        {
            if (evidence.SlotStates[(int)slot] is not { } slotState)
            {
                continue;
            }

            var sidecarSlot = ParseRuntimeLogicalSlot(slotState, out var error);
            if (sidecarSlot is null)
            {
                return new LogicalSlotResolution(null, false, $"slot {slot} sidecar: {error}");
            }

            if (sidecarSlot != slot)
            {
                return new LogicalSlotResolution(null, false,
                    $"slot {slot} sidecar identifies slot {sidecarSlot}");
            }

            if (recovered is not null)
            {
                return new LogicalSlotResolution(null, false,
                    "base state is missing and multiple logical-slot sidecars exist");
            }

            recovered = slot;
        }

        if (recovered is not null)
        {
            return new LogicalSlotResolution(recovered, false, "");
        }

        if (evidence.MmNativeSaveExists)
        {
            return new LogicalSlotResolution(null, false,
                "base and logical-slot sidecars are missing while the per-seed MM native save exists");
        }

        return new LogicalSlotResolution(0, true, "");
    }
}
