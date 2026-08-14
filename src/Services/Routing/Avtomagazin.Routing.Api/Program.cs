using Avtomagazin.Contracts;
using Avtomagazin.Contracts.Events;
using Avtomagazin.Routing.Api.Consumers;
using Avtomagazin.Routing.Api.Data;
using Avtomagazin.Routing.Api.Gov;
using Avtomagazin.ServiceDefaults;
using MassTransit;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);
builder.AddAvtomagazinDefaults(bus =>
{
    bus.AddConsumer<VehiclePositionUpdatedConsumer>();
});

builder.Services.AddDbContext<RoutingDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("Routing")
                      ?? "Host=localhost;Port=5432;Database=avtomagazin_routing;Username=avtomagazin;Password=avtomagazin"));

builder.Services.AddSingleton<IGovIntegration, StubGovIntegration>();

var app = builder.Build();
app.UseAvtomagazinDefaults();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<RoutingDbContext>();
    await db.Database.EnsureCreatedAsync();
    if (db.Database.IsRelational())
    {
        await db.Database.ExecuteSqlRawAsync(
            """
            CREATE TABLE IF NOT EXISTS "PresenceReports" (
                "Id" uuid NOT NULL PRIMARY KEY,
                "StopId" uuid NOT NULL,
                "SettlementName" character varying(200) NOT NULL,
                "Kind" character varying(32) NOT NULL,
                "DeviceToken" character varying(512) NOT NULL,
                "ReportedAtUtc" timestamp with time zone NOT NULL
            );
            """);
    }
    await Seed.EnsureSeedAsync(db);
}

app.MapGet("/api/routes", async (RoutingDbContext db) =>
    await db.Routes.AsNoTracking()
        .Include(r => r.Stops.OrderBy(s => s.Sequence))
        .OrderBy(r => r.Name)
        .ToListAsync())
    .WithName("ListRoutes")
    .WithTags("Routing");

app.MapGet("/api/stops/{settlement}", async (string settlement, RoutingDbContext db) =>
{
    var stops = await db.Stops.AsNoTracking()
        .Where(s => s.SettlementName.Contains(settlement))
        .OrderBy(s => s.PlannedArrivalUtc)
        .ToListAsync();

    return Results.Ok(stops);
})
.WithName("FindStops")
.WithTags("Routing");

app.MapGet("/api/eta", async (Guid? stopId, string? settlement, RoutingDbContext db) =>
{
    var query = db.EtaSnapshots.AsNoTracking().AsQueryable();

    if (stopId is not null)
    {
        query = query.Where(e => e.StopId == stopId);
    }
    else if (!string.IsNullOrWhiteSpace(settlement))
    {
        query = query.Where(e => e.SettlementName.Contains(settlement));
    }

    var items = await query.OrderBy(e => e.EstimatedArrivalUtc).Take(50).ToListAsync();
    return Results.Ok(items);
})
.WithName("GetEta")
.WithTags("Routing");

app.MapPost("/api/routes", async (CreateRouteRequest request, RoutingDbContext db) =>
{
    if (string.IsNullOrWhiteSpace(request.Name))
    {
        return Results.BadRequest(new { error = "name is required" });
    }

    var route = new TradeRoute
    {
        Id = Guid.NewGuid(),
        Name = request.Name.Trim(),
        VehicleId = request.VehicleId
    };

    db.Routes.Add(route);
    await db.SaveChangesAsync();
    return Results.Created($"/api/routes/{route.Id}", route);
})
.RequireAuthorization(policy => policy.RequireRole(Roles.Driver, Roles.Operator, Roles.Admin))
.WithName("CreateRoute")
.WithTags("Routing");

app.MapPost("/api/routes/{routeId:guid}/stops", async (
    Guid routeId,
    CreateStopRequest request,
    RoutingDbContext db) =>
{
    var route = await db.Routes.FirstOrDefaultAsync(r => r.Id == routeId);
    if (route is null)
    {
        return Results.NotFound();
    }

    var stop = new RouteStop
    {
        Id = Guid.NewGuid(),
        RouteId = routeId,
        Sequence = request.Sequence,
        SettlementName = request.SettlementName.Trim(),
        RegionCode = request.RegionCode.Trim(),
        Latitude = request.Latitude,
        Longitude = request.Longitude,
        PlannedArrivalUtc = request.PlannedArrivalUtc.ToUniversalTime()
    };

    db.Stops.Add(stop);
    await db.SaveChangesAsync();
    return Results.Created($"/api/stops/{stop.SettlementName}", stop);
})
.RequireAuthorization(policy => policy.RequireRole(Roles.Driver, Roles.Operator, Roles.Admin))
.WithName("AddStop")
.WithTags("Routing");

app.MapPost("/api/schedule/change", async (
    ChangeScheduleRequest request,
    RoutingDbContext db,
    IPublishEndpoint bus) =>
{
    var stop = await db.Stops.FirstOrDefaultAsync(s => s.Id == request.StopId && s.RouteId == request.RouteId);
    if (stop is null)
    {
        return Results.NotFound();
    }

    var previous = stop.PlannedArrivalUtc;
    stop.PlannedArrivalUtc = request.NewArrivalUtc.ToUniversalTime();
    await db.SaveChangesAsync();

    await bus.Publish(new ScheduleChanged(
        request.RouteId,
        stop.Id,
        stop.SettlementName,
        previous,
        stop.PlannedArrivalUtc,
        request.Reason));

    return Results.Ok(stop);
})
.RequireAuthorization(policy => policy.RequireRole(Roles.Driver, Roles.Operator, Roles.Admin))
.WithName("ChangeSchedule")
.WithTags("Routing");

app.MapPost("/api/coverage/visit", async (
    CoverageVisitRequest request,
    RoutingDbContext db,
    IGovIntegration gov,
    IPublishEndpoint bus) =>
{
    var stop = await db.Stops.FirstOrDefaultAsync(s => s.Id == request.StopId);
    if (stop is null)
    {
        return Results.NotFound();
    }

    var visit = new CoverageVisit
    {
        Id = Guid.NewGuid(),
        VehicleId = request.VehicleId,
        StopId = stop.Id,
        SettlementName = stop.SettlementName,
        RegionCode = stop.RegionCode,
        ArrivedAtUtc = request.ArrivedAtUtc ?? DateTimeOffset.UtcNow,
        WithinScheduledWindow = request.WithinScheduledWindow
    };

    db.CoverageVisits.Add(visit);
    await db.SaveChangesAsync();

    var evt = new CoverageVisitRecorded(
        visit.VehicleId,
        visit.StopId,
        visit.SettlementName,
        visit.RegionCode,
        visit.ArrivedAtUtc,
        visit.WithinScheduledWindow);

    await bus.Publish(evt);
    await gov.PublishCoverageAsync(evt);

    return Results.Created($"/api/coverage/{visit.Id}", visit);
})
.RequireAuthorization(policy => policy.RequireRole(Roles.Driver, Roles.Operator, Roles.Admin))
.WithName("RecordCoverageVisit")
.WithTags("Coverage");

app.MapPost("/api/stops/{stopId:guid}/arrived", async (
    Guid stopId,
    DriverArrivedRequest request,
    RoutingDbContext db,
    IGovIntegration gov,
    IPublishEndpoint bus) =>
{
    var stop = await db.Stops.FirstOrDefaultAsync(s => s.Id == stopId);
    if (stop is null)
    {
        return Results.NotFound();
    }

    var arrivedAt = DateTimeOffset.UtcNow;
    var onTime = ReportWindow.Contains(stop.PlannedArrivalUtc, arrivedAt);
    var visit = new CoverageVisit
    {
        Id = Guid.NewGuid(),
        VehicleId = request.VehicleId,
        StopId = stop.Id,
        SettlementName = stop.SettlementName,
        RegionCode = stop.RegionCode,
        ArrivedAtUtc = arrivedAt,
        WithinScheduledWindow = onTime
    };
    db.CoverageVisits.Add(visit);
    await db.SaveChangesAsync();

    var coverage = new CoverageVisitRecorded(
        visit.VehicleId,
        visit.StopId,
        visit.SettlementName,
        visit.RegionCode,
        visit.ArrivedAtUtc,
        visit.WithinScheduledWindow);

    await bus.Publish(coverage);
    await bus.Publish(new DriverArrivedAtStop(
        request.VehicleId,
        stop.RouteId,
        stop.Id,
        stop.SettlementName,
        arrivedAt));
    await gov.PublishCoverageAsync(coverage);

    return Results.Ok(new { visit.Id, stop.SettlementName, arrivedAt, withinScheduledWindow = onTime });
})
.RequireAuthorization(policy => policy.RequireRole(Roles.Driver, Roles.Operator, Roles.Admin))
.WithName("DriverArrivedAtStop")
.WithTags("Routing");

app.MapPost("/api/stops/{stopId:guid}/reports", async (
    Guid stopId,
    PresenceReportRequest request,
    RoutingDbContext db) =>
{
    var kind = (request.Kind ?? "").Trim().ToLowerInvariant();
    if (kind is not ("on-site" or "no-show"))
    {
        return Results.BadRequest(new { error = "kind must be on-site or no-show" });
    }

    var stop = await db.Stops.FirstOrDefaultAsync(s => s.Id == stopId);
    if (stop is null)
    {
        return Results.NotFound();
    }

    var now = DateTimeOffset.UtcNow;
    if (!ReportWindow.Contains(stop.PlannedArrivalUtc, now))
    {
        return Results.Json(
            new { error = "report window is closed", opensAtUtc = stop.PlannedArrivalUtc.AddMinutes(-15), closesAtUtc = stop.PlannedArrivalUtc.AddHours(1) },
            statusCode: StatusCodes.Status403Forbidden);
    }

    var report = new StopPresenceReport
    {
        Id = Guid.NewGuid(),
        StopId = stop.Id,
        SettlementName = stop.SettlementName,
        Kind = kind,
        DeviceToken = string.IsNullOrWhiteSpace(request.DeviceToken) ? "anonymous" : request.DeviceToken.Trim(),
        ReportedAtUtc = now
    };
    db.PresenceReports.Add(report);
    await db.SaveChangesAsync();
    return Results.Created($"/api/stops/{stop.Id}/reports/{report.Id}", report);
})
.WithName("ReportStopPresence")
.WithTags("Routing");

app.MapGet("/api/presence-reports", async (RoutingDbContext db) =>
    await db.PresenceReports.AsNoTracking()
        .OrderByDescending(r => r.ReportedAtUtc)
        .Take(100)
        .ToListAsync())
.WithName("ListPresenceReports")
.WithTags("Routing");

app.MapGet("/api/coverage", async (string? regionCode, RoutingDbContext db) =>
{
    var query = db.CoverageVisits.AsNoTracking().AsQueryable();
    if (!string.IsNullOrWhiteSpace(regionCode))
    {
        query = query.Where(v => v.RegionCode == regionCode);
    }

    return Results.Ok(await query.OrderByDescending(v => v.ArrivedAtUtc).Take(100).ToListAsync());
})
.WithName("ListCoverage")
.WithTags("Coverage");

app.Run();

public sealed record CoverageVisitRequest(
    Guid VehicleId,
    Guid StopId,
    DateTimeOffset? ArrivedAtUtc,
    bool WithinScheduledWindow);

public sealed record CreateRouteRequest(string Name, Guid VehicleId);

public sealed record CreateStopRequest(
    int Sequence,
    string SettlementName,
    string RegionCode,
    double Latitude,
    double Longitude,
    DateTimeOffset PlannedArrivalUtc);

public sealed record ChangeScheduleRequest(
    Guid RouteId,
    Guid StopId,
    DateTimeOffset NewArrivalUtc,
    string Reason);

public sealed record DriverArrivedRequest(Guid VehicleId);

public sealed record PresenceReportRequest(string Kind, string? DeviceToken);

internal static class ReportWindow
{
    public static bool Contains(DateTimeOffset plannedUtc, DateTimeOffset now)
    {
        if (IsInside(plannedUtc, now))
        {
            return true;
        }

        var today = new DateTimeOffset(now.Year, now.Month, now.Day, plannedUtc.Hour, plannedUtc.Minute, 0, TimeSpan.Zero);
        return IsInside(today, now);
    }

    private static bool IsInside(DateTimeOffset plannedUtc, DateTimeOffset now)
        => now >= plannedUtc.AddMinutes(-15) && now <= plannedUtc.AddHours(1);
}

public partial class Program;
