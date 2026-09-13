using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Avtomagazin.Notifications.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace Avtomagazin.Notifications.Api;

internal static class NotificationDevices
{
    public const int MinTokenLength = 16;

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

    public static async Task<(DeviceSubscription? Device, IResult? Error)> TryBindAsync(
        NotificationsDbContext db,
        string? deviceToken,
        string platform,
        string? settlementName,
        Guid userId)
    {
        var token = (deviceToken ?? "").Trim();
        if (token.Length < MinTokenLength)
        {
            return (null, Results.BadRequest(new { error = "deviceToken слишком короткий" }));
        }

        var existing = await db.DeviceSubscriptions.FirstOrDefaultAsync(d => d.DeviceToken == token);
        if (existing?.UserId is Guid owner && owner != userId)
        {
            return (null, Results.Conflict(new { error = "Этот токен устройства уже привязан к другому аккаунту" }));
        }

        return (await UpsertAsync(db, token, platform, settlementName ?? "", userId), null);
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
