using System.Security.Claims;
using Avtomagazin.Contracts;
using Avtomagazin.Notifications.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace Avtomagazin.Notifications.Api.Endpoints;

public static class NotificationEndpoints
{
    public static WebApplication MapNotificationApi(this WebApplication app)
    {
        var group = app.MapGroup("/api").WithTags("Notifications");

        group.MapPost("/devices/register", async (
                DeviceRegistrationRequest request,
                ClaimsPrincipal principal,
                NotificationsDbContext db) =>
            {
                var userId = NotificationDevices.CurrentUserId(principal);
                if (userId is null)
                {
                    return Results.Unauthorized();
                }

                var device = await NotificationDevices.UpsertAsync(
                    db,
                    request.DeviceToken,
                    request.Platform,
                    request.SettlementName,
                    userId);
                await db.SaveChangesAsync();
                return Results.Ok(device.ToDto());
            })
            .RequireAuthorization()
            .WithName("RegisterDevice");

        group.MapPost("/favorites", async (
                FavoriteStopRequest request,
                ClaimsPrincipal principal,
                NotificationsDbContext db) =>
            {
                var userId = NotificationDevices.CurrentUserId(principal);
                if (userId is null)
                {
                    return Results.Unauthorized();
                }

                if (string.IsNullOrWhiteSpace(request.DeviceToken) || request.StopId == Guid.Empty)
                {
                    return Results.BadRequest(new { error = "deviceToken and stopId are required" });
                }

                var device = await NotificationDevices.UpsertAsync(
                    db,
                    request.DeviceToken,
                    request.Platform ?? "web",
                    request.SettlementName,
                    userId);

                var existing = await db.FavoriteStops
                    .FirstOrDefaultAsync(f => f.UserId == userId && f.StopId == request.StopId);

                if (existing is null)
                {
                    existing = new FavoriteStop
                    {
                        Id = Guid.NewGuid(),
                        UserId = userId,
                        DeviceSubscriptionId = device.Id,
                        StopId = request.StopId,
                        SettlementName = request.SettlementName,
                        CreatedAtUtc = DateTimeOffset.UtcNow
                    };
                    db.FavoriteStops.Add(existing);
                }
                else
                {
                    existing.SettlementName = request.SettlementName;
                    existing.DeviceSubscriptionId = device.Id;
                }

                await db.SaveChangesAsync();
                return Results.Ok(existing.ToDto());
            })
            .RequireAuthorization()
            .WithName("AddFavorite");

        group.MapDelete("/favorites/{stopId:guid}", async (
                Guid stopId,
                ClaimsPrincipal principal,
                NotificationsDbContext db) =>
            {
                var userId = NotificationDevices.CurrentUserId(principal);
                if (userId is null)
                {
                    return Results.Unauthorized();
                }

                var favorite = await db.FavoriteStops
                    .FirstOrDefaultAsync(f => f.UserId == userId && f.StopId == stopId);

                if (favorite is null)
                {
                    return Results.NotFound();
                }

                db.FavoriteStops.Remove(favorite);
                await db.SaveChangesAsync();
                return Results.NoContent();
            })
            .RequireAuthorization()
            .WithName("RemoveFavorite");

        group.MapGet("/favorites", async (ClaimsPrincipal principal, NotificationsDbContext db) =>
            {
                var userId = NotificationDevices.CurrentUserId(principal);
                if (userId is null)
                {
                    return Results.Unauthorized();
                }

                var items = await db.FavoriteStops
                    .AsNoTracking()
                    .Where(f => f.UserId == userId)
                    .OrderBy(f => f.SettlementName)
                    .ToListAsync();

                return Results.Ok(items.Select(f => f.ToDto()));
            })
            .RequireAuthorization()
            .WithName("ListFavorites");

        group.MapGet("/favorites/stats", async (NotificationsDbContext db) =>
            {
                var items = await db.FavoriteStops
                    .AsNoTracking()
                    .Where(f => f.UserId != null)
                    .GroupBy(f => new { f.StopId, f.SettlementName })
                    .Select(g => new
                    {
                        g.Key.StopId,
                        g.Key.SettlementName,
                        Count = g.Count()
                    })
                    .OrderByDescending(x => x.Count)
                    .ThenBy(x => x.SettlementName)
                    .ToListAsync();

                return Results.Ok(items.Select(x => new FavoriteStatsDto(x.StopId, x.SettlementName, x.Count)));
            })
            .RequireAuthorization(policy => policy.RequireRole(Roles.Operator, Roles.Admin, Roles.Driver))
            .WithName("FavoriteStats");

        group.MapGet("/notifications", async (string? settlement, NotificationsDbContext db) =>
            {
                var query = db.NotificationLogs.AsNoTracking().AsQueryable();
                if (!string.IsNullOrWhiteSpace(settlement))
                {
                    query = query.Where(n => n.SettlementName == settlement);
                }

                return Results.Ok((await query.OrderByDescending(n => n.SentAtUtc).Take(100).ToListAsync())
                    .Select(n => n.ToDto()));
            })
            .RequireAuthorization(policy => policy.RequireRole(Roles.Operator, Roles.Admin, Roles.Driver))
            .WithName("ListNotifications");

        return app;
    }
}
