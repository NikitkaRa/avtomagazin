using Avtomagazin.Contracts;
using Avtomagazin.Contracts.Events;
using Avtomagazin.Routing.Api.Data;
using Avtomagazin.ServiceDefaults;
using MassTransit;
using Microsoft.EntityFrameworkCore;

namespace Avtomagazin.Routing.Api.Endpoints;

public static class RouteEndpoints
{
    public static RouteGroupBuilder MapRouteEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api").WithTags("Routing");

        group.MapGet("/routes", async (bool? catalog, RoutingDbContext db, CancellationToken ct) =>
                Results.Ok(await RouteList.BuildAsync(db, catalog == true, ct)))
            .RequireAuthorization()
            .WithName("ListRoutes");

        group.MapGet("/routes/{routeId:guid}", async (Guid routeId, RoutingDbContext db) =>
            {
                var route = await db.Routes.AsNoTracking()
                    .Include(r => r.Stops.OrderBy(s => s.Sequence))
                    .FirstOrDefaultAsync(r => r.Id == routeId);
                return route is null ? Results.NotFound() : Results.Ok(route);
            })
            .RequireAuthorization()
            .WithName("GetRoute");

        group.MapGet("/stops/{settlement}", async (string settlement, RoutingDbContext db) =>
            {
                var stops = await db.Stops.AsNoTracking()
                    .Where(s => s.SettlementName.Contains(settlement))
                    .OrderBy(s => s.PlannedArrivalUtc)
                    .ToListAsync();
                return Results.Ok(stops);
            })
            .RequireAuthorization()
            .WithName("FindStops");

        group.MapPost("/routes", async (CreateRouteRequest request, RoutingDbContext db) =>
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
            .WithName("CreateRoute");

        group.MapPatch("/routes/{routeId:guid}", async (Guid routeId, UpdateRouteRequest request, RoutingDbContext db) =>
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
            .WithName("UpdateRoute");

        group.MapDelete("/routes/{routeId:guid}", async (Guid routeId, RoutingDbContext db) =>
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
            .WithName("DeleteRoute");

        group.MapPut("/routes/{routeId:guid}/stops", async (
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
            .WithName("ReplaceRouteStops");

        group.MapPost("/routes/{routeId:guid}/stops", async (
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
            .WithName("AddStop");

        group.MapDelete("/stops/{stopId:guid}", async (Guid stopId, RoutingDbContext db) =>
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
            .WithName("DeleteStop");

        group.MapPost("/schedule/change", async (
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
            .WithName("ChangeSchedule");

        group.MapPost("/stops/{stopId:guid}/reports", async (
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
                if (!ScheduleWindow.Contains(stop.PlannedArrivalUtc, now))
                {
                    return Results.Json(
                        new
                        {
                            error = "report window is closed",
                            opensAtUtc = stop.PlannedArrivalUtc.AddMinutes(-15),
                            closesAtUtc = stop.PlannedArrivalUtc.AddHours(1)
                        },
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
            .WithName("ReportStopPresence");

        group.MapGet("/presence-reports", async (RoutingDbContext db) =>
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
            .RequireAuthorization(policy => policy.RequireRole(Roles.Driver, Roles.Seller, Roles.Operator, Roles.Admin))
            .WithName("ListPresenceReports");

        return group;
    }
}
