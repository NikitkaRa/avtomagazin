using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Avtomagazin.Contracts;
using Avtomagazin.Contracts.Events;
using Avtomagazin.Notifications.Api.Consumers;
using Avtomagazin.Notifications.Api.Data;
using Avtomagazin.Notifications.Api.Push;
using Avtomagazin.ServiceDefaults;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);
builder.AddAvtomagazinDefaults(bus =>
{
    bus.AddConsumer<DriverArrivedAtStopConsumer>();
    bus.AddConsumer<ScheduleChangedConsumer>();
});

var notificationsCs = DeploySecrets.ConnectionString(
    builder.Configuration,
    builder.Environment,
    "Notifications",
    "Host=localhost;Port=5432;Database=avtomagazin_notifications;Username=avtomagazin;Password=avtomagazin");

builder.Services.AddDbContext<NotificationsDbContext>(options =>
{
    if (builder.Environment.IsEnvironment("Testing"))
    {
        options.UseInMemoryDatabase("avtomagazin-notifications-tests");
        return;
    }

    options.UseNpgsql(notificationsCs);
});

if (!builder.Environment.IsEnvironment("Testing"))
{
    builder.AddAvtomagazinPostgresHealth("postgres", notificationsCs);
}

if (FcmPushSender.IsConfigured(builder.Configuration))
{
    builder.Services.AddHttpClient<IPushSender, FcmPushSender>();
}
else
{
    builder.Services.AddSingleton<IPushSender, LoggingPushSender>();
}

var app = builder.Build();
app.UseAvtomagazinDefaults();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<NotificationsDbContext>();
    await db.Database.EnsureCreatedAsync();
    if (db.Database.IsRelational())
    {
        await db.Database.ExecuteSqlRawAsync(
            """
            CREATE TABLE IF NOT EXISTS "FavoriteStops" (
                "Id" uuid NOT NULL PRIMARY KEY,
                "UserId" uuid,
                "DeviceSubscriptionId" uuid NOT NULL,
                "StopId" uuid NOT NULL,
                "SettlementName" character varying(200) NOT NULL,
                "CreatedAtUtc" timestamp with time zone NOT NULL
            );
            ALTER TABLE "FavoriteStops" ADD COLUMN IF NOT EXISTS "UserId" uuid;
            CREATE UNIQUE INDEX IF NOT EXISTS "IX_FavoriteStops_DeviceSubscriptionId_StopId"
                ON "FavoriteStops" ("DeviceSubscriptionId", "StopId");
            CREATE UNIQUE INDEX IF NOT EXISTS "IX_FavoriteStops_UserId_StopId"
                ON "FavoriteStops" ("UserId", "StopId")
                WHERE "UserId" IS NOT NULL;
            """);
    }
}

app.MapPost("/api/devices/register", async (
    DeviceRegistrationRequest request,
    ClaimsPrincipal principal,
    NotificationsDbContext db) =>
{
    var userId = CurrentUserId(principal);
    if (userId is null)
    {
        return Results.Unauthorized();
    }

    var device = await UpsertDeviceAsync(db, request.DeviceToken, request.Platform, request.SettlementName, userId);
    await db.SaveChangesAsync();
    return Results.Ok(new
    {
        device.Id,
        device.DeviceToken,
        device.Platform,
        device.SettlementName,
        device.UserId
    });
})
.RequireAuthorization()
.WithName("RegisterDevice")
.WithTags("Notifications");

app.MapPost("/api/favorites", async (
    FavoriteStopRequest request,
    ClaimsPrincipal principal,
    NotificationsDbContext db) =>
{
    var userId = CurrentUserId(principal);
    if (userId is null)
    {
        return Results.Unauthorized();
    }

    if (string.IsNullOrWhiteSpace(request.DeviceToken) || request.StopId == Guid.Empty)
    {
        return Results.BadRequest(new { error = "deviceToken and stopId are required" });
    }

    var device = await UpsertDeviceAsync(
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
    return Results.Ok(new
    {
        existing.Id,
        existing.StopId,
        existing.SettlementName,
        existing.UserId,
        deviceToken = device.DeviceToken
    });
})
.RequireAuthorization()
.WithName("AddFavorite")
.WithTags("Notifications");

app.MapDelete("/api/favorites/{stopId:guid}", async (
    Guid stopId,
    ClaimsPrincipal principal,
    NotificationsDbContext db) =>
{
    var userId = CurrentUserId(principal);
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
.WithName("RemoveFavorite")
.WithTags("Notifications");

app.MapGet("/api/favorites", async (ClaimsPrincipal principal, NotificationsDbContext db) =>
{
    var userId = CurrentUserId(principal);
    if (userId is null)
    {
        return Results.Unauthorized();
    }

    var items = await db.FavoriteStops
        .AsNoTracking()
        .Where(f => f.UserId == userId)
        .OrderBy(f => f.SettlementName)
        .Select(f => new { f.StopId, f.SettlementName, f.CreatedAtUtc })
        .ToListAsync();

    return Results.Ok(items);
})
.RequireAuthorization()
.WithName("ListFavorites")
.WithTags("Notifications");

app.MapGet("/api/favorites/stats", async (NotificationsDbContext db) =>
{
    var items = await db.FavoriteStops
        .AsNoTracking()
        .Where(f => f.UserId != null)
        .GroupBy(f => new { f.StopId, f.SettlementName })
        .Select(g => new
        {
            g.Key.StopId,
            g.Key.SettlementName,
            subscriberCount = g.Count()
        })
        .OrderByDescending(x => x.subscriberCount)
        .ThenBy(x => x.SettlementName)
        .ToListAsync();

    return Results.Ok(items);
})
.RequireAuthorization(policy => policy.RequireRole(Roles.Operator, Roles.Admin, Roles.Driver))
.WithName("FavoriteStats")
.WithTags("Notifications");

app.MapGet("/api/notifications", async (string? settlement, NotificationsDbContext db) =>
{
    var query = db.NotificationLogs.AsNoTracking().AsQueryable();
    if (!string.IsNullOrWhiteSpace(settlement))
    {
        query = query.Where(n => n.SettlementName == settlement);
    }

    return Results.Ok(await query.OrderByDescending(n => n.SentAtUtc).Take(100).ToListAsync());
})
.RequireAuthorization(policy => policy.RequireRole(Roles.Operator, Roles.Admin, Roles.Driver))
.WithName("ListNotifications")
.WithTags("Notifications");

app.Run();

static Guid? CurrentUserId(ClaimsPrincipal user)
{
    if (user.Identity?.IsAuthenticated != true)
    {
        return null;
    }

    var raw = user.FindFirstValue(JwtRegisteredClaimNames.Sub)
              ?? user.FindFirstValue(ClaimTypes.NameIdentifier);
    return Guid.TryParse(raw, out var id) ? id : null;
}

static async Task<DeviceSubscription> UpsertDeviceAsync(
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

public partial class Program;
