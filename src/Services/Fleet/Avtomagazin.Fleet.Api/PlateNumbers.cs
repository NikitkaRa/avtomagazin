using System.Text.RegularExpressions;

namespace Avtomagazin.Fleet.Api;

internal static partial class PlateNumbers
{
    public static bool TryNormalize(string? raw, out string normalized, out string? error)
    {
        normalized = "";
        if (string.IsNullOrWhiteSpace(raw))
        {
            error = "Укажите госномер";
            return false;
        }

        var compact = CompactRegex().Replace(raw.Trim().ToUpperInvariant(), "");
        if (!PlateBodyRegex().IsMatch(compact))
        {
            error = "Формат номера: 1234 AB-7 (4 цифры, 2 буквы, цифра)";
            return false;
        }

        normalized = $"{compact[..4]} {compact[4..6]}-{compact[6]}";
        error = null;
        return true;
    }

    [GeneratedRegex(@"[^A-Z0-9]")]
    private static partial Regex CompactRegex();

    [GeneratedRegex(@"^\d{4}[A-Z]{2}\d$")]
    private static partial Regex PlateBodyRegex();
}

internal static class VehiclePhotos
{
    public static string? TrimOrNull(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
