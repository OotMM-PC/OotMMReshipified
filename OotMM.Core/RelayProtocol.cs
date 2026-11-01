using System.Text;

namespace OotMM.Core;

public static class RelayProtocol
{
    public const int MultiworldProtocolVersion = 4;
    public const int AnchorProtocolVersion = 3;
    public const ushort DefaultPort = 42801;

    public const string Hello = "OOTMM_HELLO";
    public const string Ready = "OOTMM_READY";
    public const string Status = "OOTMM_STATUS";
    public const string ProtocolError = "OOTMM_PROTOCOL_ERROR";
    public const string Ping = "OOTMM_PING";
    public const string Pong = "OOTMM_PONG";
    public const string SeedManifest = "OOTMM_SEED_MANIFEST";
    public const string SessionMeta = "OOTMM_SESSION_META";
    public const string Chat = "OOTMM_CHAT";
    public const string Teleport = "OOTMM_TELEPORT";
    public const string AdminUnlockAll = "OOTMM_ADMIN_UNLOCK_ALL";
    public const string AdminUnlockCheck = "OOTMM_ADMIN_UNLOCK_CHECK";
    public const string WorldFlag = "OOTMM_WORLD_FLAG";

    public const string CheckComplete = "OOTMM_CHECK_COMPLETE";
    public const string GiveItem = "OOTMM_GIVE_ITEM";
    public const string DeliveryStored = "OOTMM_DELIVERY_STORED";
    public const string CheckClaim = "OOTMM_CHECK_CLAIM";

    public static string BuildHello(Seed seed, string saveId, string clientName, uint logicalSlot)
    {
        var text = new StringBuilder();
        text.Append("{\"type\":\"").Append(Hello)
            .Append("\",\"protocolVersion\":").Append(MultiworldProtocolVersion)
            .Append(",\"format\":").Append(JsonText.EscapeString(seed.Format))
            .Append(",\"generatorVersion\":").Append(JsonText.EscapeString(seed.GeneratorVersion))
            .Append(",\"seedId\":").Append(JsonText.EscapeString(seed.SeedId))
            .Append(",\"settingsHash\":").Append(JsonText.EscapeString(seed.SettingsHash))
            .Append(",\"teamId\":").Append(JsonText.EscapeString(seed.TeamId))
            .Append(",\"playerId\":").Append(seed.PlayerId)
            .Append(",\"playerCount\":").Append(seed.PlayerCount)
            .Append(",\"saveId\":").Append(JsonText.EscapeString(saveId))
            .Append(",\"logicalSlot\":").Append(logicalSlot)
            .Append(",\"sessionEpoch\":").Append(JsonText.EscapeString(seed.SessionEpoch.ToString()))
            .Append(",\"clientName\":").Append(JsonText.EscapeString(clientName))
            .Append('}');
        return text.ToString();
    }

    public static bool FrameAllowedBeforeProtocolReady(string type) =>
        type == Pong || type == Ready || type == ProtocolError;

    public static string ExtractStringField(string line, string key)
    {
        var needle = "\"" + key + "\"";
        var start = line.IndexOf(needle, StringComparison.Ordinal);
        if (start < 0)
        {
            return "";
        }

        var cursor = start + needle.Length;
        while (cursor < line.Length && line[cursor] is ' ' or '\t')
        {
            cursor++;
        }

        if (cursor >= line.Length || line[cursor] != ':')
        {
            return "";
        }

        cursor++;
        while (cursor < line.Length && line[cursor] is ' ' or '\t')
        {
            cursor++;
        }

        if (cursor >= line.Length || line[cursor] != '"')
        {
            return "";
        }

        var valueStart = cursor + 1;
        var end = line.IndexOf('"', valueStart);
        return end < 0 ? "" : line[valueStart..end];
    }

    public static int ExtractIntField(string line, string key, int fallback = 0)
    {
        var needle = "\"" + key + "\"";
        var cursor = line.IndexOf(needle, StringComparison.Ordinal);
        if (cursor < 0)
        {
            return fallback;
        }

        cursor += needle.Length;
        while (cursor < line.Length && line[cursor] is ' ' or '\t' or ':')
        {
            cursor++;
        }

        var start = cursor;
        while (cursor < line.Length && line[cursor] is >= '0' and <= '9')
        {
            cursor++;
        }

        if (cursor == start)
        {
            return fallback;
        }

        return int.TryParse(line[start..cursor], out var value) ? value : fallback;
    }

    public static string ReplaceIntField(string line, string key, int value)
    {
        var needle = "\"" + key + "\"";
        var cursor = line.IndexOf(needle, StringComparison.Ordinal);
        if (cursor < 0)
        {
            return line;
        }

        cursor += needle.Length;
        while (cursor < line.Length && line[cursor] is ' ' or '\t' or ':')
        {
            cursor++;
        }

        var start = cursor;
        while (cursor < line.Length && line[cursor] is >= '0' and <= '9')
        {
            cursor++;
        }

        return cursor == start ? line : string.Concat(line.AsSpan(0, start), value.ToString(), line.AsSpan(cursor));
    }

    public static string AppendStringField(string line, string key, string value)
    {
        var end = line.LastIndexOf('}');
        return end < 0
            ? line
            : line[..end] + ",\"" + key + "\":" + JsonText.EscapeString(value) + line[end..];
    }

    public static ulong ExtractU64Field(string line, string key, ulong fallback = 0)
    {
        var needle = "\"" + key + "\"";
        var cursor = line.IndexOf(needle, StringComparison.Ordinal);
        if (cursor < 0)
        {
            return fallback;
        }

        cursor += needle.Length;
        while (cursor < line.Length && line[cursor] is ' ' or '\t' or ':')
        {
            cursor++;
        }

        var quoted = cursor < line.Length && line[cursor] == '"';
        if (quoted)
        {
            cursor++;
        }

        var start = cursor;
        var value = 0ul;
        while (cursor < line.Length && line[cursor] is >= '0' and <= '9')
        {
            var digit = (ulong)(line[cursor] - '0');
            if (value > (ulong.MaxValue - digit) / 10)
            {
                return fallback;
            }

            value = value * 10 + digit;
            cursor++;
        }

        if (cursor == start || (quoted && (cursor >= line.Length || line[cursor] != '"')))
        {
            return fallback;
        }

        return value;
    }
}
