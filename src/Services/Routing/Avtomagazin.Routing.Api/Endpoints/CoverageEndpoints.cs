using System.Security.Claims;
using Avtomagazin.Contracts;
using Avtomagazin.Routing.Api.Data;
using Avtomagazin.Routing.Api.Gov;
using Avtomagazin.ServiceDefaults;
using MassTransit;
using Microsoft.EntityFrameworkCore;

namespace Avtomagazin.Routing.Api.Endpoints;

public static class CoverageEndpoints
{
    public static RouteGroupBuilder MapCoverageEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api").WithTags("Coverage");

        group.MapPost("/coverage/visit", async (
                CoverageVisitRequest request,
                ClaimsPrincipal principal,
                RoutingDbContext db,
                IGovIntegration gov,
                IPublishEndpoint bus,
                CancellationToken ct) =>
            {
                if (HttpAccess.ForbidVehicleWrite(principal, request.VehicleId) is { } denied)
                {
                    return denied;
                }

                var stop = await CoverageVisits.FindStopAsync(db, request.StopId, ct);
                if (stop is null)
                {
                    return Results.NotFound();
                }

                var arrivedAt = request.ArrivedAtUtc ?? DateTimeOffset.UtcNow;
                var visit = CoverageVisits.Create(request.VehicleId, stop, arrivedAt);
                await CoverageVisits.SaveAsync(db, visit, ct);
                await CoverageVisits.PublishCoverageAsync(visit, bus, gov, ct);

                return Results.Created($"/api/coverage/{visit.Id}", visit.ToDto());
            })
            .RequireAuthorization(policy => policy.RequireRole(Roles.Driver, Roles.Seller, Roles.Operator, Roles.Admin))
            .WithName("RecordCoverageVisit");

        group.MapPost("/stops/{stopId:guid}/arrived", async (
                Guid stopId,
                DriverArrivedRequest request,
                ClaimsPrincipal principal,
                RoutingDbContext db,
                IGovIntegration gov,
                IPublishEndpoint bus,
                CancellationToken ct) =>
            {
                if (HttpAccess.ForbidVehicleWrite(principal, request.VehicleId) is { } denied)
                {
                    return denied;
                }

                var stop = await CoverageVisits.FindStopAsync(db, stopId, ct);
                if (stop is null)
                {
                    return Results.NotFound();
                }

                var arrivedAt = DateTimeOffset.UtcNow;
                var skipped = request.Skipped;
                var visit = CoverageVisits.Create(request.VehicleId, stop, arrivedAt, skipped);
                await CoverageVisits.SaveAsync(db, visit, ct);

                if (!skipped)
                {
                    await CoverageVisits.PublishCoverageAsync(visit, bus, gov, ct);
                    await CoverageVisits.PublishDriverArrivalAsync(request.VehicleId, stop, arrivedAt, bus, ct);
                }

                return Results.Ok(visit.ToDto());
            })
            .RequireAuthorization(policy => policy.RequireRole(Roles.Driver, Roles.Seller, Roles.Operator, Roles.Admin))
            .WithName("DriverArrivedAtStop");

        group.MapGet("/coverage", async (string? regionCode, RoutingDbContext db) =>
            {
                var query = db.CoverageVisits.AsNoTracking().AsQueryable();
                if (!string.IsNullOrWhiteSpace(regionCode))
                {
                    query = query.Where(v => v.RegionCode == regionCode);
                }

                return Results.Ok((await query.OrderByDescending(v => v.ArrivedAtUtc).Take(100).ToListAsync())
                    .Select(v => v.ToDto()));
            })
            .RequireAuthorization(policy => policy.RequireRole(Roles.Driver, Roles.Seller, Roles.Operator, Roles.Admin))
            .WithName("ListCoverage");

        return group;
    }
}
