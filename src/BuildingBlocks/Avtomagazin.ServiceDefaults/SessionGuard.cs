using System.Net.Http.Json;
using System.Security.Claims;
using Avtomagazin.Contracts;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace Avtomagazin.ServiceDefaults;

public interface ISessionGuard
{
    Task<bool> ValidateAsync(ClaimsPrincipal principal, CancellationToken ct = default);
}

public sealed class NoopSessionGuard : ISessionGuard
{
    public Task<bool> ValidateAsync(ClaimsPrincipal principal, CancellationToken ct = default)
        => Task.FromResult(true);
}

public sealed class HttpSessionGuard(
    HttpClient http,
    IMemoryCache cache,
    IConfiguration config) : ISessionGuard
{
    public async Task<bool> ValidateAsync(ClaimsPrincipal principal, CancellationToken ct = default)
    {
        var userId = principal.UserId();
        if (userId is null)
        {
            return false;
        }

        var claimed = principal.TokenVersion();
        try
        {
            var snapshot = await cache.GetOrCreateAsync($"session:{userId}", async entry =>
            {
                entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(15);
                using var request = new HttpRequestMessage(HttpMethod.Get, $"api/internal/users/{userId}/session");
                var key = config["Internal:Key"];
                if (!string.IsNullOrWhiteSpace(key))
                {
                    request.Headers.TryAddWithoutValidation("X-Internal-Key", key);
                }

                using var response = await http.SendAsync(request, ct);
                var json = new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                {
                    return new SessionSnapshot(UserStatuses.Disabled, -1);
                }

                if (!response.IsSuccessStatusCode)
                {
                    entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(2);
                    return new SessionSnapshot(UserStatuses.Active, claimed, Unreachable: true);
                }

                var identity = await response.Content.ReadFromJsonAsync<IdentitySession>(json, ct);
                return identity is null
                    ? new SessionSnapshot(UserStatuses.Disabled, -1)
                    : new SessionSnapshot(identity.Status, identity.TokenVersion);
            });

            if (snapshot is null || snapshot.Unreachable)
            {
                return true;
            }

            return snapshot.Status == UserStatuses.Active
                   && snapshot.TokenVersion == claimed;
        }
        catch
        {
            return true;
        }
    }

    private sealed record IdentitySession(string Status, int TokenVersion);

    private sealed record SessionSnapshot(string Status, int TokenVersion, bool Unreachable = false);
}

public static class DeploySecrets
{
    public const string DemoJwtKey = "dev-only-change-me-avtomagazin-super-secret-key-32b";
    public const string DemoInternalKey = "dev-internal-key";

    public static string JwtKey(IConfiguration config, IHostEnvironment env)
    {
        var key = config["Jwt:Key"];
        if (!string.IsNullOrWhiteSpace(key))
        {
            if (IsPublic(env) && !IsStrongJwt(key))
            {
                throw new InvalidOperationException("Jwt:Key must be 32+ chars and not the demo key.");
            }

            return key;
        }

        if (IsPublic(env))
        {
            throw new InvalidOperationException("Jwt:Key is required.");
        }

        return DemoJwtKey;
    }

    public static string ConnectionString(IConfiguration config, IHostEnvironment env, string name, string? devFallback)
    {
        var value = config.GetConnectionString(name);
        if (!string.IsNullOrWhiteSpace(value))
        {
            return value;
        }

        if (IsPublic(env) || string.IsNullOrWhiteSpace(devFallback))
        {
            throw new InvalidOperationException($"ConnectionStrings:{name} is required.");
        }

        return devFallback;
    }

    public static void EnsureInternalKey(IConfiguration config, IHostEnvironment env)
    {
        if (!IsPublic(env))
        {
            return;
        }

        var key = config["Internal:Key"];
        if (string.IsNullOrWhiteSpace(key) || key == DemoInternalKey)
        {
            throw new InvalidOperationException("Internal:Key is required and must not be the demo value.");
        }
    }

    public static bool IsPublic(IHostEnvironment env)
        => env.IsProduction() || env.IsEnvironment("Staging");

    private static bool IsStrongJwt(string key)
        => key.Length >= 32 && !string.Equals(key, DemoJwtKey, StringComparison.Ordinal);
}
