using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Avtomagazin.Notifications.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace Avtomagazin.Notifications.Api;

internal static class NotificationDevices
{
    public static Guid? CurrentUserId(ClaimsPrincipal user)
    {
        if (user.Identity?.IsAuthenticated != true)
        {
            return null;
        }

        var raw = user.FindFirstValue(JwtRegisteredClaimNames.Sub)
                  ?? user.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(raw, out var id) ? id : null;
    }

    public static async Task<DeviceSubscription> UpsertAsync(
        NotificationsDbContext db,
        string deviceToken,
        string platform,
        string settlementName,
        Guid? userId)
    {
        var existing = await db.DeviceSubscriptions
            .FirstOrDefaultAsync(d => d.DeviceToken == deviceToken);

        if (existing is null)
        {
            existing = new DeviceSubscription
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                DeviceToken = deviceToken,
                Platform = platform,
                SettlementName = settlementName,
                CreatedAtUtc = DateTimeOffset.UtcNow
            };
            db.DeviceSubscriptions.Add(existing);
            return existing;
        }

        existing.UserId = userId ?? existing.UserId;
        existing.Platform = platform;
        if (!string.IsNullOrWhiteSpace(settlementName))
        {
            existing.SettlementName = settlementName;
        }

        return existing;
    }
}

public sealed record DeviceRegistrationRequest(
    Guid? UserId,
    string DeviceToken,
    string Platform,
    string SettlementName);

public sealed record FavoriteStopRequest(
    Guid? UserId,
    string DeviceToken,
    string? Platform,
    Guid StopId,
    string SettlementName);
