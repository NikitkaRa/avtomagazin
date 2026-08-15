using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Avtomagazin.Notifications.Api.Push;

public sealed class FcmPushSender(HttpClient http, IConfiguration config, ILogger<FcmPushSender> logger) : IPushSender
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };
    private readonly SemaphoreSlim _gate = new(1, 1);
    private string? _accessToken;
    private DateTimeOffset _accessTokenExpires;

    public static bool IsConfigured(IConfiguration config)
        => TryReadAccount(config) is not null;

    public async Task SendAsync(string deviceToken, string title, string body, CancellationToken cancellationToken = default)
    {
        var account = TryReadAccount(config);
        if (account is null)
        {
            logger.LogWarning("FCM is not configured; skip push");
            return;
        }

        try
        {
            var access = await AccessTokenAsync(account, cancellationToken);
            using var request = new HttpRequestMessage(
                HttpMethod.Post,
                $"https://fcm.googleapis.com/v1/projects/{account.ProjectId}/messages:send");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", access);
            request.Content = JsonContent.Create(new
            {
                message = new
                {
                    token = deviceToken,
                    notification = new { title, body }
                }
            });

            using var response = await http.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                var detail = await response.Content.ReadAsStringAsync(cancellationToken);
                logger.LogWarning("FCM {Status}: {Detail}", (int)response.StatusCode, detail);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "FCM send failed");
        }
    }

    private async Task<string> AccessTokenAsync(FcmAccount account, CancellationToken ct)
    {
        if (_accessToken is not null && _accessTokenExpires > DateTimeOffset.UtcNow.AddMinutes(2))
        {
            return _accessToken;
        }

        await _gate.WaitAsync(ct);
        try
        {
            if (_accessToken is not null && _accessTokenExpires > DateTimeOffset.UtcNow.AddMinutes(2))
            {
                return _accessToken;
            }

            using var rsa = RSA.Create();
            rsa.ImportFromPem(account.PrivateKey.ToCharArray());
            var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var header = Base64Url("""{"alg":"RS256","typ":"JWT"}"""u8.ToArray());
            var payload = Base64Url(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new
            {
                iss = account.ClientEmail,
                scope = "https://www.googleapis.com/auth/firebase.messaging",
                aud = "https://oauth2.googleapis.com/token",
                iat = now,
                exp = now + 3600
            })));
            var unsigned = $"{header}.{payload}";
            var signature = rsa.SignData(Encoding.ASCII.GetBytes(unsigned), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            var assertion = $"{unsigned}.{Base64Url(signature)}";

            using var tokenRequest = new HttpRequestMessage(HttpMethod.Post, "https://oauth2.googleapis.com/token")
            {
                Content = new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["grant_type"] = "urn:ietf:params:oauth:grant-type:jwt-bearer",
                    ["assertion"] = assertion
                })
            };
            using var tokenResponse = await http.SendAsync(tokenRequest, ct);
            tokenResponse.EnsureSuccessStatusCode();
            var granted = await tokenResponse.Content.ReadFromJsonAsync<GoogleToken>(Json, ct)
                          ?? throw new InvalidOperationException("empty FCM token");
            _accessToken = granted.AccessToken;
            _accessTokenExpires = DateTimeOffset.UtcNow.AddSeconds(Math.Max(60, granted.ExpiresIn - 60));
            return _accessToken;
        }
        finally
        {
            _gate.Release();
        }
    }

    internal static FcmAccount? TryReadAccount(IConfiguration config)
    {
        var json = config["Fcm:ServiceAccountJson"];
        var path = config["Fcm:ServiceAccountPath"];
        if (string.IsNullOrWhiteSpace(json) && !string.IsNullOrWhiteSpace(path) && File.Exists(path))
        {
            json = File.ReadAllText(path);
        }

        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            var account = JsonSerializer.Deserialize<FcmAccount>(json, Json);
            if (account is null
                || string.IsNullOrWhiteSpace(account.ProjectId)
                || string.IsNullOrWhiteSpace(account.ClientEmail)
                || string.IsNullOrWhiteSpace(account.PrivateKey))
            {
                return null;
            }

            account.PrivateKey = account.PrivateKey.Replace("\\n", "\n", StringComparison.Ordinal);
            return account;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string Base64Url(byte[] data)
        => Convert.ToBase64String(data).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private sealed record GoogleToken(
        [property: JsonPropertyName("access_token")] string AccessToken,
        [property: JsonPropertyName("expires_in")] int ExpiresIn);
}

internal sealed class FcmAccount
{
    [JsonPropertyName("project_id")]
    public string ProjectId { get; set; } = "";

    [JsonPropertyName("client_email")]
    public string ClientEmail { get; set; } = "";

    [JsonPropertyName("private_key")]
    public string PrivateKey { get; set; } = "";
}
