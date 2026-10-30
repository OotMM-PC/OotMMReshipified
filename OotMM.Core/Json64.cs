using System.Text.Json;

namespace OotMM.Core;

public static class Json64
{
    public static ulong? ReadU64(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Number)
        {
            const double largestExactInteger = 9007199254740991.0;
            var number = element.GetDouble();
            if (double.IsNaN(number) || number < 0.0 || number > largestExactInteger
                || Math.Floor(number) != number)
            {
                return null;
            }

            return (ulong)number;
        }

        if (element.ValueKind == JsonValueKind.String)
        {
            var text = element.GetString();
            return text is { Length: > 0 and <= 20 }
                   && ulong.TryParse(text, System.Globalization.NumberStyles.None,
                                     System.Globalization.CultureInfo.InvariantCulture, out var parsed)
                ? parsed
                : null;
        }

        return null;
    }

    public static string WriteU64(ulong value) => value.ToString();
}
