using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Avtomagazin.ServiceDefaults;

public sealed class ObjectStorageOptions
{
    public string? Endpoint { get; set; }
    public string? PublicBaseUrl { get; set; }
    public string Bucket { get; set; } = "avtomagazin";
    public string AccessKey { get; set; } = "avtomagazin";
    public string SecretKey { get; set; } = "avtomagazin";
    public bool ForcePathStyle { get; set; } = true;
}

public interface IObjectStorage
{
    bool IsConfigured { get; }
    bool IsOwnedUrl(string url);
    Task<string> UploadAsync(string folder, byte[] bytes, string contentType, CancellationToken ct = default);
    Task DeleteByUrlAsync(string? url, CancellationToken ct = default);
}

public static class ObjectStorageServiceExtensions
{
    public static IHostApplicationBuilder AddAvtomagazinObjectStorage(this IHostApplicationBuilder builder)
    {
        builder.Services.Configure<ObjectStorageOptions>(builder.Configuration.GetSection("Storage"));
        builder.Services.AddSingleton<IObjectStorage, S3ObjectStorage>();
        return builder;
    }
}

internal sealed class S3ObjectStorage(
    IOptions<ObjectStorageOptions> options,
    ILogger<S3ObjectStorage> log) : IObjectStorage, IDisposable
{
    private readonly ObjectStorageOptions _opt = options.Value;
    private readonly IAmazonS3? _client = CreateClient(options.Value);

    public bool IsConfigured => _client is not null && !string.IsNullOrWhiteSpace(_opt.Endpoint);

    public bool IsOwnedUrl(string url)
        => TryOwnedUri(url, _opt) is not null;

    public async Task<string> UploadAsync(string folder, byte[] bytes, string contentType, CancellationToken ct = default)
    {
        if (_client is null)
        {
            throw new InvalidOperationException("Object storage не настроен (Storage:Endpoint).");
        }

        await EnsureBucketAsync(ct);
        var ext = contentType switch
        {
            "image/png" => "png",
            "image/webp" => "webp",
            _ => "jpg"
        };
        var key = $"{folder.Trim('/')}/{Guid.NewGuid():N}.{ext}";
        await using var stream = new MemoryStream(bytes);
        var put = new PutObjectRequest
        {
            BucketName = _opt.Bucket,
            Key = key,
            InputStream = stream,
            ContentType = contentType,
            CannedACL = S3CannedACL.PublicRead,
            AutoCloseStream = false
        };
        await _client.PutObjectAsync(put, ct);
        return PublicUrl(key);
    }

    public async Task DeleteByUrlAsync(string? url, CancellationToken ct = default)
    {
        if (_client is null || string.IsNullOrWhiteSpace(url) || !TryKeyFromUrl(url, out var key))
        {
            return;
        }

        try
        {
            await _client.DeleteObjectAsync(_opt.Bucket, key, ct);
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "Failed to delete object {Key}", key);
        }
    }

    private string PublicUrl(string key)
    {
        var root = (_opt.PublicBaseUrl ?? _opt.Endpoint ?? "").TrimEnd('/');
        return $"{root}/{_opt.Bucket}/{key}";
    }

    private bool TryKeyFromUrl(string url, out string key)
    {
        key = TryOwnedUri(url, _opt)?.Key ?? "";
        return key.Length > 0;
    }

    private static (string Host, string Key)? TryOwnedUri(string url, ObjectStorageOptions opt)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)
            || uri.Scheme is not ("http" or "https"))
        {
            return null;
        }

        var marker = $"/{opt.Bucket}/";
        var idx = uri.AbsolutePath.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (idx < 0)
        {
            return null;
        }

        var key = uri.AbsolutePath[(idx + marker.Length)..].TrimStart('/');
        if (key.Length == 0)
        {
            return null;
        }

        foreach (var raw in new[] { opt.PublicBaseUrl, opt.Endpoint })
        {
            if (string.IsNullOrWhiteSpace(raw)
                || !Uri.TryCreate(raw, UriKind.Absolute, out var owned))
            {
                continue;
            }

            if (string.Equals(uri.Host, owned.Host, StringComparison.OrdinalIgnoreCase))
            {
                return (uri.Host, Uri.UnescapeDataString(key));
            }
        }

        return null;
    }

    private async Task EnsureBucketAsync(CancellationToken ct)
    {
        if (_client is null)
        {
            return;
        }

        try
        {
            await _client.PutBucketAsync(new PutBucketRequest { BucketName = _opt.Bucket }, ct);
        }
        catch
        {
            // Bucket may already exist.
        }
    }

    private static IAmazonS3? CreateClient(ObjectStorageOptions opt)
    {
        if (string.IsNullOrWhiteSpace(opt.Endpoint))
        {
            return null;
        }

        var config = new AmazonS3Config
        {
            ServiceURL = opt.Endpoint.TrimEnd('/'),
            ForcePathStyle = opt.ForcePathStyle,
            AuthenticationRegion = "us-east-1"
        };
        return new AmazonS3Client(opt.AccessKey, opt.SecretKey, config);
    }

    public void Dispose() => _client?.Dispose();
}

public static class MediaPhotos
{
    private static readonly HashSet<string> Allowed = new(StringComparer.OrdinalIgnoreCase)
    {
        "image/jpeg", "image/jpg", "image/png", "image/webp"
    };

    public static async Task<(string? Url, string? Error)> StoreAsync(
        IObjectStorage storage,
        string folder,
        string? dataUrlOrHttpUrl,
        string? previousUrl,
        bool clear,
        CancellationToken ct = default)
    {
        if (clear)
        {
            if (!string.IsNullOrWhiteSpace(previousUrl) && previousUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            {
                await storage.DeleteByUrlAsync(previousUrl, ct);
            }

            return (null, null);
        }

        if (string.IsNullOrWhiteSpace(dataUrlOrHttpUrl))
        {
            return (previousUrl, null);
        }

        var value = dataUrlOrHttpUrl.Trim();
        if (value.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            || value.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            if (string.Equals(value, previousUrl, StringComparison.OrdinalIgnoreCase))
            {
                return (previousUrl, null);
            }

            if (storage.IsOwnedUrl(value))
            {
                return (value, null);
            }

            return (null, "Фото: загрузите файл, произвольные ссылки не принимаются");
        }

        if (!TryDecodeDataUrl(value, out var bytes, out var mime, out var error))
        {
            return (null, error);
        }

        if (!storage.IsConfigured)
        {
            return (null, "Хранилище фото не настроено. Поднимите MinIO (docker compose) и Storage:Endpoint.");
        }

        var url = await storage.UploadAsync(folder, bytes, mime, ct);
        if (!string.IsNullOrWhiteSpace(previousUrl)
            && previousUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(previousUrl, url, StringComparison.Ordinal))
        {
            await storage.DeleteByUrlAsync(previousUrl, ct);
        }

        return (url, null);
    }

    public static bool TryDecodeDataUrl(string value, out byte[] bytes, out string mime, out string? error)
    {
        bytes = [];
        mime = "image/jpeg";
        error = null;

        if (!value.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
        {
            error = "Фото: ожидается data URL или http(s) ссылка";
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

        mime = meta[(mimeStart + 1)..mimeEnd];
        if (!Allowed.Contains(mime))
        {
            error = "Фото: только JPEG, PNG или WebP";
            return false;
        }

        try
        {
            bytes = Convert.FromBase64String(payload);
        }
        catch (FormatException)
        {
            error = "Фото: неверный base64";
            return false;
        }

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

        return true;
    }
}
