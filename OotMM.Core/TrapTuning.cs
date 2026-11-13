using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace OotMM.Core;

public sealed record KnockbackChance(int Percent, int Distance);

public sealed class TrapTuning
{
    public const int FramesPerSecond = 20;
    public const int MaxDamageQuarters = 80;
    public const int MinIceBreakoutPercent = 25;
    public const int MaxIceBreakoutPercent = 400;
    public const double MaxFireSeconds = 30;
    public const double MinShockSeconds = 0.5;
    public const double MaxShockSeconds = 10;
    public const int MaxKnockbackDistance = 2000;

    public int IceBreakoutPercent { get; set; } = 100;
    public int IceDamageQuarters { get; set; }
    public int FireDamageQuarters { get; set; }
    public double FireSeconds { get; set; } = 10;
    public int ShockDamageQuarters { get; set; }
    public double ShockSeconds { get; set; } = 1;
    public string Knockback { get; set; } = "";

    public TrapTuning Normalized()
    {
        return new TrapTuning
        {
            IceBreakoutPercent = Math.Clamp(IceBreakoutPercent, MinIceBreakoutPercent, MaxIceBreakoutPercent),
            IceDamageQuarters = Math.Clamp(IceDamageQuarters, 0, MaxDamageQuarters),
            FireDamageQuarters = Math.Clamp(FireDamageQuarters, 0, MaxDamageQuarters),
            FireSeconds = double.IsFinite(FireSeconds) ? Math.Clamp(FireSeconds, 0, MaxFireSeconds) : 10,
            ShockDamageQuarters = Math.Clamp(ShockDamageQuarters, 0, MaxDamageQuarters),
            ShockSeconds = double.IsFinite(ShockSeconds) ? Math.Clamp(ShockSeconds, MinShockSeconds, MaxShockSeconds) : 1,
            Knockback = TryParseKnockback(Knockback, out _, out _) ? (Knockback ?? "").Trim() : "",
        };
    }

    [JsonIgnore]
    public int FireFrames => (int)Math.Round(Normalized().FireSeconds * FramesPerSecond);

    [JsonIgnore]
    public int ShockFrames => Math.Max(1, (int)Math.Round(Normalized().ShockSeconds * FramesPerSecond));

    [JsonIgnore]
    public IReadOnlyList<KnockbackChance> KnockbackChances =>
        TryParseKnockback(Knockback, out var rows, out _) ? rows : [];

    [JsonIgnore]
    public bool IsOriginal
    {
        get
        {
            var traps = Normalized();
            return traps.IceBreakoutPercent == 100 && traps.IceDamageQuarters == 0 && traps.FireDamageQuarters == 0
                   && traps.FireSeconds.Equals(10.0) && traps.ShockDamageQuarters == 0 && traps.ShockSeconds.Equals(1.0)
                   && traps.Knockback.Length == 0;
        }
    }

    public string ToMetaJson()
    {
        var traps = Normalized();
        return JsonSerializer.Serialize(new
        {
            iceBreakoutPercent = traps.IceBreakoutPercent,
            iceDamageQuarters = traps.IceDamageQuarters,
            fireDamageQuarters = traps.FireDamageQuarters,
            fireSeconds = traps.FireSeconds,
            shockDamageQuarters = traps.ShockDamageQuarters,
            shockSeconds = traps.ShockSeconds,
            knockback = traps.Knockback,
        });
    }

    public static TrapTuning FromMetaLine(string line)
    {
        try
        {
            if (JsonNode.Parse(line) is not JsonObject root || root["traps"] is not JsonObject traps)
            {
                return new TrapTuning();
            }

            var defaults = new TrapTuning();
            return new TrapTuning
            {
                IceBreakoutPercent = Read(traps, "iceBreakoutPercent", defaults.IceBreakoutPercent),
                IceDamageQuarters = Read(traps, "iceDamageQuarters", defaults.IceDamageQuarters),
                FireDamageQuarters = Read(traps, "fireDamageQuarters", defaults.FireDamageQuarters),
                FireSeconds = Read(traps, "fireSeconds", defaults.FireSeconds),
                ShockDamageQuarters = Read(traps, "shockDamageQuarters", defaults.ShockDamageQuarters),
                ShockSeconds = Read(traps, "shockSeconds", defaults.ShockSeconds),
                Knockback = traps["knockback"] is JsonValue knockback && knockback.TryGetValue(out string? text)
                    ? text ?? ""
                    : "",
            }.Normalized();
        }
        catch (JsonException)
        {
            return new TrapTuning();
        }
    }

    private static T Read<T>(JsonObject node, string name, T fallback) =>
        node[name] is JsonValue value && value.TryGetValue(out T? result) && result is not null ? result : fallback;

    public static bool TryParseKnockback(string? text, out List<KnockbackChance> rows, out string error)
    {
        rows = [];
        error = "";
        var source = (text ?? "").Trim().Trim('[', ']').Trim();
        if (source.Length == 0)
        {
            return true;
        }

        var total = 0;
        foreach (var raw in source.Split([',', ';'], StringSplitOptions.RemoveEmptyEntries))
        {
            var part = raw.Trim();
            if (part.Length == 0)
            {
                continue;
            }

            var colon = part.IndexOf(':');
            if (colon <= 0 || colon == part.Length - 1)
            {
                error = $"'{part}' should look like 25:200 (chance:distance).";
                return false;
            }

            var chanceText = part[..colon].Trim().TrimEnd('%').Trim();
            var distanceText = part[(colon + 1)..].Trim();
            if (!int.TryParse(chanceText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var chance)
                || chance <= 0 || chance > 100)
            {
                error = $"'{part}': the chance must be a whole percent from 1 to 100.";
                return false;
            }

            if (!int.TryParse(distanceText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var distance)
                || distance < 0 || distance > MaxKnockbackDistance)
            {
                error = $"'{part}': the distance must be from 0 to {MaxKnockbackDistance}.";
                return false;
            }

            total += chance;
            if (total > 100)
            {
                error = "The chances add up to more than 100%.";
                return false;
            }

            rows.Add(new KnockbackChance(chance, distance));
        }

        return true;
    }
}
