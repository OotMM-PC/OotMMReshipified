using System.Text;
using System.Text.Json;

namespace OotMM.Core;

/// <summary>The boot.json handoff the launcher writes for each game process.</summary>
public sealed class BootConfig
{
    public string SaveId { get; set; } = "";
    public string SeedPath { get; set; } = "";
    public string StatePath { get; set; } = "";
    public string InboxPath { get; set; } = "";
    public string OutboxPath { get; set; } = "";
    public string PresenceOutPath { get; set; } = "";
    public string PresenceInPath { get; set; } = "";
    public string MmSaveDir { get; set; } = "";
    public string NativeSaveSubdir { get; set; } = "";
    public string SelftestReportPath { get; set; } = "";
    public string SelftestMode { get; set; } = "";
    public bool AllowDebugMenus { get; set; }
    public int MultiworldProtocol { get; set; } = 3;
    public Game BootGame { get; set; } = Game.Oot;
    public uint? BootEntrance { get; set; }
    public uint? OotAge { get; set; }
    public uint? LogicalSlot { get; set; }
    public ulong? ParentWindow { get; set; }

    /// <summary>Byte-identical to the C++ writer: optional fields are omitted
    /// when empty/default, parentWindow is a decimal string.</summary>
    public string Serialize()
    {
        var outText = new StringBuilder();
        outText.Append("{\"saveId\":").Append(JsonText.EscapeString(SaveId));
        AppendString(outText, "seedPath", SeedPath, always: true);
        AppendString(outText, "statePath", StatePath, always: true);
        AppendString(outText, "inboxPath", InboxPath, always: true);
        AppendString(outText, "outboxPath", OutboxPath, always: true);
        AppendString(outText, "presenceOutPath", PresenceOutPath);
        AppendString(outText, "presenceInPath", PresenceInPath);
        AppendString(outText, "mmSaveDir", MmSaveDir);
        AppendString(outText, "nativeSaveSubdir", NativeSaveSubdir);
        AppendString(outText, "selftestReportPath", SelftestReportPath);
        AppendString(outText, "selftestMode", SelftestMode);
        outText.Append(",\"allowDebugMenus\":").Append(AllowDebugMenus ? "true" : "false");
        if (MultiworldProtocol != 3)
        {
            outText.Append(",\"multiworldProtocol\":").Append(MultiworldProtocol);
        }

        outText.Append(",\"bootGame\":").Append(JsonText.EscapeString(GameInfo.ToSeedTag(BootGame)));
        if (BootEntrance is { } bootEntrance)
        {
            outText.Append(",\"bootEntrance\":").Append(bootEntrance);
        }

        if (OotAge is { } ootAge)
        {
            outText.Append(",\"ootAge\":").Append(ootAge);
        }

        if (LogicalSlot is { } logicalSlot)
        {
            outText.Append(",\"logicalSlot\":").Append(logicalSlot);
        }

        if (ParentWindow is { } parentWindow)
        {
            outText.Append(",\"parentWindow\":").Append(JsonText.EscapeString(parentWindow.ToString()));
        }

        return outText.Append('}').ToString();
    }

    /// <summary>Tolerant parse; null only for non-object input or an invalid
    /// logicalSlot (anything outside 0-2 fails the whole parse).</summary>
    public static BootConfig? Parse(string jsonText)
    {
        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(jsonText);
        }
        catch (JsonException)
        {
            return null;
        }

        using (doc)
        {
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            var config = new BootConfig
            {
                SaveId = ReadString(root, "saveId"),
                SeedPath = ReadString(root, "seedPath"),
                StatePath = ReadString(root, "statePath"),
                InboxPath = ReadString(root, "inboxPath"),
                OutboxPath = ReadString(root, "outboxPath"),
                PresenceOutPath = ReadString(root, "presenceOutPath"),
                PresenceInPath = ReadString(root, "presenceInPath"),
                MmSaveDir = ReadString(root, "mmSaveDir"),
                NativeSaveSubdir = ReadString(root, "nativeSaveSubdir"),
                SelftestReportPath = ReadString(root, "selftestReportPath"),
                SelftestMode = ReadString(root, "selftestMode"),
            };

            if (root.TryGetProperty("allowDebugMenus", out var allowDebug)
                && allowDebug.ValueKind is JsonValueKind.True or JsonValueKind.False)
            {
                config.AllowDebugMenus = allowDebug.GetBoolean();
            }

            if (root.TryGetProperty("multiworldProtocol", out var protocol)
                && protocol.ValueKind == JsonValueKind.Number
                && protocol.TryGetInt32(out var protocolValue) && protocolValue >= 0)
            {
                config.MultiworldProtocol = protocolValue;
            }

            if (root.TryGetProperty("bootGame", out var bootGame)
                && bootGame.ValueKind == JsonValueKind.String
                && GameInfo.TryFromSeedTag(bootGame.GetString()!) is { } game)
            {
                config.BootGame = game;
            }

            config.BootEntrance = ReadU32(root, "bootEntrance");
            config.OotAge = ReadU32(root, "ootAge");

            if (root.TryGetProperty("logicalSlot", out var logicalSlot))
            {
                if (logicalSlot.ValueKind != JsonValueKind.Number
                    || !logicalSlot.TryGetUInt32(out var slot) || slot > 2)
                {
                    return null;
                }

                config.LogicalSlot = slot;
            }

            if (root.TryGetProperty("parentWindow", out var parentWindow))
            {
                if (parentWindow.ValueKind == JsonValueKind.String)
                {
                    if (ulong.TryParse(parentWindow.GetString(), out var handle))
                    {
                        config.ParentWindow = handle;
                    }
                }
                else if (parentWindow.ValueKind == JsonValueKind.Number
                         && parentWindow.TryGetUInt64(out var numericHandle))
                {
                    config.ParentWindow = numericHandle;
                }
            }

            return config;
        }
    }

    public static BootConfig? LoadFromFile(string path)
    {
        try
        {
            return Parse(File.ReadAllText(path));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static void AppendString(StringBuilder outText, string key, string value, bool always = false)
    {
        if (always || value.Length > 0)
        {
            outText.Append(",\"").Append(key).Append("\":").Append(JsonText.EscapeString(value));
        }
    }

    private static string ReadString(JsonElement root, string key) =>
        root.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()!
            : "";

    private static uint? ReadU32(JsonElement root, string key) =>
        root.TryGetProperty(key, out var value)
        && value.ValueKind == JsonValueKind.Number
        && value.TryGetUInt32(out var parsed)
            ? parsed
            : null;
}
