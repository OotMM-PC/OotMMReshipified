using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace OotMM.Core;

public sealed record KnockbackChance(int Percent, int MinDistance, int MaxDistance)
{
    public bool IsRange => MinDistance != MaxDistance;
}

public sealed class TrapTuning
{
    public const int FramesPerSecond = 20;
    public const int OriginalDamagePercent = 100;
    public const int MaxDamagePercent = 1000;
    public const int MinIceBreakoutPercent = 25;
    public const int MaxIceBreakoutPercent = 400;
    public const double MaxFireSeconds = 30;
    public const double MinShockSeconds = 0.5;
    public const double MaxShockSeconds = 10;
    public const int MaxKnockbackDistance = 2000;
    public const string OriginalKnockback = "100:0-600";

    public int IceBreakoutPercent { get; set; } = 100;
    public int IceDamagePercent { get; set; } = OriginalDamagePercent;
    public int FireDamagePercent { get; set; } = OriginalDamagePercent;
    public double FireSeconds { get; set; } = 10;
    public int ShockDamagePercent { get; set; } = OriginalDamagePercent;
    public double ShockSeconds { get; set; } = 1;
    public string Knockback { get; set; } = OriginalKnockback;

    public TrapTuning Normalized()
    {
        var knockback = (Knockback ?? "").Trim();
        return new TrapTuning
        {
            IceBreakoutPercent = Math.Clamp(IceBreakoutPercent, MinIceBreakoutPercent, MaxIceBreakoutPercent),
            IceDamagePercent = Math.Clamp(IceDamagePercent, 0, MaxDamagePercent),
            FireDamagePercent = Math.Clamp(FireDamagePercent, 0, MaxDamagePercent),
            FireSeconds = double.IsFinite(FireSeconds) ? Math.Clamp(FireSeconds, 0, MaxFireSeconds) : 10,
            ShockDamagePercent = Math.Clamp(ShockDamagePercent, 0, MaxDamagePercent),
            ShockSeconds = double.IsFinite(ShockSeconds) ? Math.Clamp(ShockSeconds, MinShockSeconds, MaxShockSeconds) : 1,
            Knockback = knockback.Length > 0 && TryParseKnockback(knockback, out _, out _) ? knockback : OriginalKnockback,
        };
    }

    [JsonIgnore]
    public int FireFrames => (int)Math.Round(Normalized().FireSeconds * FramesPerSecond);

    [JsonIgnore]
    public int ShockFrames => Math.Max(1, (int)Math.Round(Normalized().ShockSeconds * FramesPerSecond));

    [JsonIgnore]
    public IReadOnlyList<KnockbackChance> KnockbackChances =>
        TryParseKnockback(Normalized().Knockback, out var rows, out _) ? rows : [];

    [JsonIgnore]
    public bool IsOriginal
    {
        get
        {
            var traps = Normalized();
            return traps.IceBreakoutPercent == 100 && traps.IceDamagePercent == OriginalDamagePercent
                   && traps.FireDamagePercent == OriginalDamagePercent && traps.FireSeconds.Equals(10.0)
                   && traps.ShockDamagePercent == OriginalDamagePercent && traps.ShockSeconds.Equals(1.0)
                   && traps.KnockbackChances.SequenceEqual([new KnockbackChance(100, 0, 600)]);
        }
    }

    public string ToMetaJson()
    {
        var traps = Normalized();
        return JsonSerializer.Serialize(new
        {
            iceBreakoutPercent = traps.IceBreakoutPercent,
            iceDamagePercent = traps.IceDamagePercent,
            fireDamagePercent = traps.FireDamagePercent,
            fireSeconds = traps.FireSeconds,
            shockDamagePercent = traps.ShockDamagePercent,
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
                IceDamagePercent = Read(traps, "iceDamagePercent", defaults.IceDamagePercent),
                FireDamagePercent = Read(traps, "fireDamagePercent", defaults.FireDamagePercent),
                FireSeconds = Read(traps, "fireSeconds", defaults.FireSeconds),
                ShockDamagePercent = Read(traps, "shockDamagePercent", defaults.ShockDamagePercent),
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
            error = "Enter at least one chance, for example 100:0-600.";
            return false;
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
                error = $"'{part}' should look like 25:200 or 25:100-300 (chance:distance).";
                return false;
            }

            var chanceText = part[..colon].Trim().TrimEnd('%').Trim();
            if (!int.TryParse(chanceText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var chance)
                || chance <= 0 || chance > 100)
            {
                error = $"'{part}': the chance must be a whole percent from 1 to 100.";
                return false;
            }

            var distanceText = part[(colon + 1)..].Trim();
            var dash = distanceText.IndexOf('-', 1);
            var minText = dash > 0 ? distanceText[..dash].Trim() : distanceText;
            var maxText = dash > 0 ? distanceText[(dash + 1)..].Trim() : distanceText;
            if (!TryParseDistance(minText, out var min) || !TryParseDistance(maxText, out var max))
            {
                error = $"'{part}': distances must be from 0 to {MaxKnockbackDistance}.";
                return false;
            }

            if (max < min)
            {
                error = $"'{part}': the range must go from the smaller distance to the larger one.";
                return false;
            }

            total += chance;
            if (total > 100)
            {
                error = "The chances add up to more than 100%.";
                return false;
            }

            rows.Add(new KnockbackChance(chance, min, max));
        }

        if (rows.Count == 0)
        {
            error = "Enter at least one chance, for example 100:0-600.";
            return false;
        }

        return true;
    }

    private static bool TryParseDistance(string text, out int distance) =>
        int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out distance)
        && distance >= 0 && distance <= MaxKnockbackDistance;
}
