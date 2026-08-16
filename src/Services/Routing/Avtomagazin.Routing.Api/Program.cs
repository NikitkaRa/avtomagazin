using System.Security.Claims;
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
builder.AddAvtomagazinObjectStorage();

var routingCs = DeploySecrets.ConnectionString(
    builder.Configuration,
    builder.Environment,
    "Routing",
    "Host=localhost;Port=5432;Database=avtomagazin_routing;Username=avtomagazin;Password=avtomagazin");

builder.Services.AddDbContext<RoutingDbContext>(options =>
{
    if (builder.Environment.IsEnvironment("Testing"))
    {
        options.UseInMemoryDatabase("avtomagazin-routing-tests");
        return;
    }

    options.UseNpgsql(routingCs);
});

if (!builder.Environment.IsEnvironment("Testing"))
{
    builder.AddAvtomagazinPostgresHealth("postgres", routingCs);
}

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
            CREATE TABLE IF NOT EXISTS "Cases" (
                "Id" uuid NOT NULL PRIMARY KEY,
                "SettlementKey" character varying(120) NOT NULL,
                "SettlementName" character varying(200) NOT NULL,
                "VehicleId" uuid,
                "Status" character varying(32) NOT NULL,
                "ReportCount" integer NOT NULL,
                "OpenedAtUtc" timestamp with time zone NOT NULL,
                "UpdatedAtUtc" timestamp with time zone NOT NULL,
                "ClosedAtUtc" timestamp with time zone
            );
            CREATE TABLE IF NOT EXISTS "CaseEvents" (
                "Id" uuid NOT NULL PRIMARY KEY,
                "CaseId" uuid NOT NULL,
                "Kind" character varying(32) NOT NULL,
                "Body" character varying(2000) NOT NULL,
                "AuthorName" character varying(200),
                "AuthorEmail" character varying(320),
                "StopId" uuid,
                "StopLabel" character varying(200),
                "FromStatus" character varying(32),
                "ToStatus" character varying(32),
                "CreatedAtUtc" timestamp with time zone NOT NULL
            );
            """);
        await db.Database.ExecuteSqlRawAsync(
            """ALTER TABLE "Stops" ADD COLUMN IF NOT EXISTS "PhotoDataUrl" text""");
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

app.MapGet("/api/routes/{routeId:guid}", async (Guid routeId, RoutingDbContext db) =>
{
    var route = await db.Routes.AsNoTracking()
        .Include(r => r.Stops.OrderBy(s => s.Sequence))
        .FirstOrDefaultAsync(r => r.Id == routeId);
    return route is null ? Results.NotFound() : Results.Ok(route);
})
.WithName("GetRoute")
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
    return Results.Created($"/api/routes/{route.Id}", new
    {
        route.Id,
        route.Name,
        route.VehicleId,
        Stops = Array.Empty<object>()
    });
})
.RequireAuthorization(policy => policy.RequireRole(Roles.Operator, Roles.Admin))
.WithName("CreateRoute")
.WithTags("Routing");

app.MapPatch("/api/routes/{routeId:guid}", async (Guid routeId, UpdateRouteRequest request, RoutingDbContext db) =>
{
    var route = await db.Routes.Include(r => r.Stops).FirstOrDefaultAsync(r => r.Id == routeId);
    if (route is null)
    {
        return Results.NotFound();
    }

    if (!string.IsNullOrWhiteSpace(request.Name))
    {
        route.Name = request.Name.Trim();
    }

    if (request.VehicleId is Guid vehicleId && vehicleId != Guid.Empty)
    {
        route.VehicleId = vehicleId;
    }

    await db.SaveChangesAsync();
    return Results.Ok(await db.Routes.AsNoTracking()
        .Include(r => r.Stops.OrderBy(s => s.Sequence))
        .FirstAsync(r => r.Id == routeId));
})
.RequireAuthorization(policy => policy.RequireRole(Roles.Operator, Roles.Admin))
.WithName("UpdateRoute")
.WithTags("Routing");

app.MapDelete("/api/routes/{routeId:guid}", async (Guid routeId, RoutingDbContext db) =>
{
    var route = await db.Routes.Include(r => r.Stops).FirstOrDefaultAsync(r => r.Id == routeId);
    if (route is null)
    {
        return Results.NotFound();
    }

    if (route.Stops.Count > 0)
    {
        db.Stops.RemoveRange(route.Stops);
    }

    db.Routes.Remove(route);
    await db.SaveChangesAsync();
    return Results.NoContent();
})
.RequireAuthorization(policy => policy.RequireRole(Roles.Operator, Roles.Admin))
.WithName("DeleteRoute")
.WithTags("Routing");

app.MapPut("/api/routes/{routeId:guid}/stops", async (
    Guid routeId,
    ReplaceStopsRequest request,
    RoutingDbContext db,
    IObjectStorage storage) =>
{
    var route = await db.Routes.Include(r => r.Stops).FirstOrDefaultAsync(r => r.Id == routeId);
    if (route is null)
    {
        return Results.NotFound();
    }

    var incoming = (request.Stops ?? [])
        .Where(s => !string.IsNullOrWhiteSpace(s.SettlementName))
        .OrderBy(s => s.Sequence <= 0 ? int.MaxValue : s.Sequence)
        .ToList();

    var keptIds = new HashSet<Guid>();
    var now = DateTimeOffset.UtcNow;
    var seq = 1;
    foreach (var item in incoming)
    {
        RouteStop stop;
        if (item.Id is Guid existingId
            && existingId != Guid.Empty
            && route.Stops.FirstOrDefault(s => s.Id == existingId) is { } found)
        {
            stop = found;
            keptIds.Add(existingId);
        }
        else
        {
            stop = new RouteStop
            {
                Id = Guid.NewGuid(),
                RouteId = routeId,
                SettlementName = "",
                RegionCode = "BY-MI"
            };
            route.Stops.Add(stop);
            keptIds.Add(stop.Id);
        }

        stop.Sequence = seq++;
        stop.SettlementName = item.SettlementName.Trim();
        stop.RegionCode = string.IsNullOrWhiteSpace(item.RegionCode) ? "BY-MI" : item.RegionCode.Trim();
        stop.Latitude = item.Latitude;
        stop.Longitude = item.Longitude;
        stop.PlannedArrivalUtc = (item.PlannedArrivalUtc ?? now.AddMinutes(15 * seq)).ToUniversalTime();
        if (item.ClearPhoto == true || !string.IsNullOrWhiteSpace(item.PhotoDataUrl))
        {
            var (photo, photoError) = await MediaPhotos.StoreAsync(
                storage,
                "stops",
                item.ClearPhoto == true ? null : item.PhotoDataUrl,
                stop.PhotoDataUrl,
                clear: item.ClearPhoto == true);
            if (photoError is not null)
            {
                return Results.BadRequest(new { error = $"«{stop.SettlementName}»: {photoError}" });
            }

            stop.PhotoDataUrl = photo;
        }
    }

    var remove = route.Stops.Where(s => !keptIds.Contains(s.Id)).ToList();
    if (remove.Count > 0)
    {
        db.Stops.RemoveRange(remove);
    }

    await db.SaveChangesAsync();
    return Results.Ok(await db.Routes.AsNoTracking()
        .Include(r => r.Stops.OrderBy(s => s.Sequence))
        .FirstAsync(r => r.Id == routeId));
})
.RequireAuthorization(policy => policy.RequireRole(Roles.Operator, Roles.Admin))
.WithName("ReplaceRouteStops")
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
.RequireAuthorization(policy => policy.RequireRole(Roles.Operator, Roles.Admin))
.WithName("AddStop")
.WithTags("Routing");

app.MapDelete("/api/stops/{stopId:guid}", async (Guid stopId, RoutingDbContext db) =>
{
    var stop = await db.Stops.FirstOrDefaultAsync(s => s.Id == stopId);
    if (stop is null)
    {
        return Results.NotFound();
    }

    db.Stops.Remove(stop);
    await db.SaveChangesAsync();
    return Results.NoContent();
})
.RequireAuthorization(policy => policy.RequireRole(Roles.Operator, Roles.Admin))
.WithName("DeleteStop")
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
.RequireAuthorization(policy => policy.RequireRole(Roles.Operator, Roles.Admin))
.WithName("ChangeSchedule")
.WithTags("Routing");

app.MapPost("/api/coverage/visit", async (
    CoverageVisitRequest request,
    ClaimsPrincipal principal,
    RoutingDbContext db,
    IGovIntegration gov,
    IPublishEndpoint bus) =>
{
    if (HttpAccess.ForbidVehicleWrite(principal, request.VehicleId) is { } denied)
    {
        return denied;
    }
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
    ClaimsPrincipal principal,
    RoutingDbContext db,
    IGovIntegration gov,
    IPublishEndpoint bus) =>
{
    if (HttpAccess.ForbidVehicleWrite(principal, request.VehicleId) is { } denied)
    {
        return denied;
    }
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

    if (kind == "no-show")
    {
        await CaseWorkflow.AttachNoShowAsync(db, stop, now);
    }

    await db.SaveChangesAsync();
    return Results.Created($"/api/stops/{stop.Id}/reports/{report.Id}", new
    {
        report.Id,
        report.StopId,
        report.SettlementName,
        report.Kind,
        report.ReportedAtUtc
    });
})
.RequireAuthorization()
.WithName("ReportStopPresence")
.WithTags("Routing");

app.MapGet("/api/presence-reports", async (RoutingDbContext db) =>
    await db.PresenceReports.AsNoTracking()
        .OrderByDescending(r => r.ReportedAtUtc)
        .Take(100)
        .Select(r => new
        {
            r.Id,
            r.StopId,
            r.SettlementName,
            r.Kind,
            r.ReportedAtUtc
        })
        .ToListAsync())
.RequireAuthorization(policy => policy.RequireRole(Roles.Driver, Roles.Operator, Roles.Admin))
.WithName("ListPresenceReports")
.WithTags("Routing");

app.MapGet("/api/cases", async (string? status, RoutingDbContext db) =>
{
    var query = db.Cases.AsNoTracking().AsQueryable();
    if (!string.IsNullOrWhiteSpace(status))
    {
        var wanted = status.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(s => s.ToLowerInvariant())
            .ToArray();
        query = query.Where(c => wanted.Contains(c.Status));
    }
    else
    {
        query = query.Where(c => c.Status == "open" || c.Status == "in_progress");
    }

    var items = await query
        .OrderByDescending(c => c.UpdatedAtUtc)
        .Take(100)
        .Select(c => new
        {
            c.Id,
            c.SettlementKey,
            c.SettlementName,
            c.VehicleId,
            c.Status,
            c.ReportCount,
            c.OpenedAtUtc,
            c.UpdatedAtUtc,
            c.ClosedAtUtc
        })
        .ToListAsync();
    return Results.Ok(items);
})
.RequireAuthorization(policy => policy.RequireRole(Roles.Driver, Roles.Operator, Roles.Admin))
.WithName("ListCases")
.WithTags("Cases");

app.MapGet("/api/cases/{id:guid}", async (Guid id, RoutingDbContext db) =>
{
    var item = await db.Cases.AsNoTracking()
        .Include(c => c.Events.OrderByDescending(e => e.CreatedAtUtc))
        .FirstOrDefaultAsync(c => c.Id == id);
    return item is null ? Results.NotFound() : Results.Ok(item);
})
.RequireAuthorization(policy => policy.RequireRole(Roles.Driver, Roles.Operator, Roles.Admin))
.WithName("GetCase")
.WithTags("Cases");

app.MapPost("/api/cases/{id:guid}/comments", async (
    Guid id,
    CaseCommentRequest request,
    ClaimsPrincipal principal,
    RoutingDbContext db) =>
{
    var body = (request.Body ?? "").Trim();
    if (body.Length == 0)
    {
        return Results.BadRequest(new { error = "Нужен текст комментария" });
    }

    var item = await db.Cases.FirstOrDefaultAsync(c => c.Id == id);
    if (item is null)
    {
        return Results.NotFound();
    }

    var now = DateTimeOffset.UtcNow;
    db.CaseEvents.Add(new CaseEvent
    {
        Id = Guid.NewGuid(),
        CaseId = item.Id,
        Kind = "comment",
        Body = body,
        AuthorName = principal.FindFirstValue("name") ?? principal.Identity?.Name,
        AuthorEmail = principal.FindFirstValue(ClaimTypes.Email) ?? principal.FindFirstValue("email"),
        CreatedAtUtc = now
    });
    item.UpdatedAtUtc = now;
    await db.SaveChangesAsync();
    return Results.Ok(await CaseWorkflow.LoadAsync(db, item.Id));
})
.RequireAuthorization(policy => policy.RequireRole(Roles.Operator, Roles.Admin))
.WithName("AddCaseComment")
.WithTags("Cases");

app.MapPatch("/api/cases/{id:guid}/status", async (
    Guid id,
    CaseStatusRequest request,
    ClaimsPrincipal principal,
    RoutingDbContext db) =>
{
    var status = (request.Status ?? "").Trim().ToLowerInvariant();
    if (status is not ("open" or "in_progress" or "closed"))
    {
        return Results.BadRequest(new { error = "status: open, in_progress или closed" });
    }

    var item = await db.Cases.FirstOrDefaultAsync(c => c.Id == id);
    if (item is null)
    {
        return Results.NotFound();
    }

    if (item.Status == status && string.IsNullOrWhiteSpace(request.Comment))
    {
        return Results.Ok(await CaseWorkflow.LoadAsync(db, item.Id));
    }

    var now = DateTimeOffset.UtcNow;
    var from = item.Status;
    item.Status = status;
    item.UpdatedAtUtc = now;
    item.ClosedAtUtc = status == "closed" ? now : null;

    var label = status switch
    {
        "open" => "открыта",
        "in_progress" => "в процессе",
        "closed" => "закрыта",
        _ => status
    };
    var note = string.IsNullOrWhiteSpace(request.Comment)
        ? $"Статус: {label}"
        : $"Статус: {label}. {request.Comment.Trim()}";

    db.CaseEvents.Add(new CaseEvent
    {
        Id = Guid.NewGuid(),
        CaseId = item.Id,
        Kind = "status",
        Body = note,
        AuthorName = principal.FindFirstValue("name") ?? principal.Identity?.Name,
        AuthorEmail = principal.FindFirstValue(ClaimTypes.Email) ?? principal.FindFirstValue("email"),
        FromStatus = from,
        ToStatus = status,
        CreatedAtUtc = now
    });

    await db.SaveChangesAsync();
    return Results.Ok(await CaseWorkflow.LoadAsync(db, item.Id));
})
.RequireAuthorization(policy => policy.RequireRole(Roles.Operator, Roles.Admin))
.WithName("SetCaseStatus")
.WithTags("Cases");

app.MapGet("/api/coverage", async (string? regionCode, RoutingDbContext db) =>
{
    var query = db.CoverageVisits.AsNoTracking().AsQueryable();
    if (!string.IsNullOrWhiteSpace(regionCode))
    {
        query = query.Where(v => v.RegionCode == regionCode);
    }

    return Results.Ok(await query.OrderByDescending(v => v.ArrivedAtUtc).Take(100).ToListAsync());
})
.RequireAuthorization(policy => policy.RequireRole(Roles.Driver, Roles.Operator, Roles.Admin))
.WithName("ListCoverage")
.WithTags("Coverage");

app.Run();

public sealed record CoverageVisitRequest(
    Guid VehicleId,
    Guid StopId,
    DateTimeOffset? ArrivedAtUtc,
    bool WithinScheduledWindow);

public sealed record CreateRouteRequest(string Name, Guid VehicleId);

public sealed record UpdateRouteRequest(string? Name, Guid? VehicleId);

public sealed record ReplaceStopsRequest(List<ReplaceStopItem>? Stops);

public sealed record ReplaceStopItem(
    Guid? Id,
    int Sequence,
    string SettlementName,
    string? RegionCode,
    double Latitude,
    double Longitude,
    DateTimeOffset? PlannedArrivalUtc,
    string? PhotoDataUrl = null,
    bool? ClearPhoto = null);

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
public sealed record CaseCommentRequest(string Body);
public sealed record CaseStatusRequest(string Status, string? Comment);

internal static class CaseWorkflow
{
    public static async Task AttachNoShowAsync(RoutingDbContext db, RouteStop stop, DateTimeOffset now)
    {
        var key = SettlementNames.Key(stop.SettlementName);
        var route = await db.Routes.AsNoTracking().FirstOrDefaultAsync(r => r.Id == stop.RouteId);
        var open = await db.Cases
            .Where(c => c.SettlementKey == key && (c.Status == "open" || c.Status == "in_progress"))
            .OrderByDescending(c => c.UpdatedAtUtc)
            .FirstOrDefaultAsync();

        if (open is null)
        {
            open = new SettlementCase
            {
                Id = Guid.NewGuid(),
                SettlementKey = key,
                SettlementName = key,
                VehicleId = route?.VehicleId,
                Status = "open",
                ReportCount = 0,
                OpenedAtUtc = now,
                UpdatedAtUtc = now
            };
            db.Cases.Add(open);
        }
        else if (open.VehicleId is null && route is not null)
        {
            open.VehicleId = route.VehicleId;
        }

        open.ReportCount += 1;
        open.UpdatedAtUtc = now;
        db.CaseEvents.Add(new CaseEvent
        {
            Id = Guid.NewGuid(),
            CaseId = open.Id,
            Kind = "report",
            Body = "Житель: не приехала",
            StopId = stop.Id,
            StopLabel = stop.SettlementName,
            CreatedAtUtc = now
        });
    }

    public static Task<SettlementCase?> LoadAsync(RoutingDbContext db, Guid id)
        => db.Cases.AsNoTracking()
            .Include(c => c.Events.OrderByDescending(e => e.CreatedAtUtc))
            .FirstOrDefaultAsync(c => c.Id == id);
}

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
