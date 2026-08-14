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

builder.Services.AddDbContext<NotificationsDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("Notifications")
                      ?? "Host=localhost;Port=5432;Database=avtomagazin_notifications;Username=avtomagazin;Password=avtomagazin"));

builder.Services.AddSingleton<IPushSender, LoggingPushSender>();

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
                "DeviceSubscriptionId" uuid NOT NULL,
                "StopId" uuid NOT NULL,
                "SettlementName" character varying(200) NOT NULL,
                "CreatedAtUtc" timestamp with time zone NOT NULL
            );
            CREATE UNIQUE INDEX IF NOT EXISTS "IX_FavoriteStops_DeviceSubscriptionId_StopId"
                ON "FavoriteStops" ("DeviceSubscriptionId", "StopId");
            """);
    }
}

app.MapPost("/api/devices/register", async (DeviceRegistrationRequest request, NotificationsDbContext db) =>
{
    var device = await UpsertDeviceAsync(db, request.DeviceToken, request.Platform, request.SettlementName, request.UserId);
    await db.SaveChangesAsync();
    return Results.Ok(new
    {
        device.Id,
        device.DeviceToken,
        device.Platform,
        device.SettlementName
    });
})
.WithName("RegisterDevice")
.WithTags("Notifications");

app.MapPost("/api/favorites", async (FavoriteStopRequest request, NotificationsDbContext db) =>
{
    if (string.IsNullOrWhiteSpace(request.DeviceToken) || request.StopId == Guid.Empty)
    {
        return Results.BadRequest(new { error = "deviceToken and stopId are required" });
    }

    var device = await UpsertDeviceAsync(
        db,
        request.DeviceToken,
        request.Platform ?? "web",
        request.SettlementName,
        request.UserId);

    var existing = await db.FavoriteStops
        .FirstOrDefaultAsync(f => f.DeviceSubscriptionId == device.Id && f.StopId == request.StopId);

    if (existing is null)
    {
        existing = new FavoriteStop
        {
            Id = Guid.NewGuid(),
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
    }

    await db.SaveChangesAsync();
    return Results.Ok(new
    {
        existing.Id,
        existing.StopId,
        existing.SettlementName,
        deviceToken = device.DeviceToken
    });
})
.WithName("AddFavorite")
.WithTags("Notifications");

app.MapDelete("/api/favorites", async (string deviceToken, Guid stopId, NotificationsDbContext db) =>
{
    var favorite = await db.FavoriteStops
        .Include(f => f.Device)
        .FirstOrDefaultAsync(f => f.Device.DeviceToken == deviceToken && f.StopId == stopId);

    if (favorite is null)
    {
        return Results.NotFound();
    }

    db.FavoriteStops.Remove(favorite);
    await db.SaveChangesAsync();
    return Results.NoContent();
})
.WithName("RemoveFavorite")
.WithTags("Notifications");

app.MapGet("/api/favorites", async (string deviceToken, NotificationsDbContext db) =>
{
    var items = await db.FavoriteStops
        .AsNoTracking()
        .Where(f => f.Device.DeviceToken == deviceToken)
        .OrderBy(f => f.SettlementName)
        .Select(f => new { f.StopId, f.SettlementName, f.CreatedAtUtc })
        .ToListAsync();

    return Results.Ok(items);
})
.WithName("ListFavorites")
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
.WithName("ListNotifications")
.WithTags("Notifications");

app.Run();

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
