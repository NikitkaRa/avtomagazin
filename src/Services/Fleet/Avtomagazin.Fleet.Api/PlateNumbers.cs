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
    private static readonly HashSet<string> Allowed = new(StringComparer.OrdinalIgnoreCase)
    {
        "image/jpeg", "image/jpg", "image/png", "image/webp"
    };

    public static string? TrimOrNull(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    public static bool TryNormalizePhoto(string? dataUrl, out string? normalized, out string? error)
    {
        normalized = null;
        error = null;
        if (string.IsNullOrWhiteSpace(dataUrl))
        {
            return true;
        }

        var value = dataUrl.Trim();
        if (!value.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
        {
            error = "Фото: ожидается data URL";
            return false;
        }

        var comma = value.IndexOf(',');
        if (comma < 0)
        {
            error = "Фото: битый data URL";
            return false;
        }

        var meta = value[..comma];
        var payload = value[(comma + 1)..];
        var mimeStart = meta.IndexOf(':');
        var mimeEnd = meta.IndexOf(';');
        if (mimeStart < 0 || mimeEnd <= mimeStart)
        {
            error = "Фото: нет MIME-типа";
            return false;
        }

        var mime = meta[(mimeStart + 1)..mimeEnd];
        if (!Allowed.Contains(mime))
        {
            error = "Фото: только JPEG, PNG или WebP";
            return false;
        }

        try
        {
            var bytes = Convert.FromBase64String(payload);
            if (bytes.Length == 0)
            {
                error = "Фото пустое";
                return false;
            }

            if (bytes.Length > 400_000)
            {
                error = "Фото больше 400 КБ — сожмите изображение";
                return false;
            }
        }
        catch (FormatException)
        {
            error = "Фото: неверный base64";
            return false;
        }

        normalized = value;
        return true;
    }
}
