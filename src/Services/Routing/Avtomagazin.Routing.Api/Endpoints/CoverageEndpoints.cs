using System.Security.Claims;
using Avtomagazin.Contracts;
using Avtomagazin.Routing.Api.Data;
using Avtomagazin.Routing.Api.Gov;
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
                var (error, visit) = await CoverageVisits.TryRecordAsync(
                    principal,
                    request.VehicleId,
                    request.StopId,
                    skipped: false,
                    db,
                    bus,
                    gov,
                    ct,
                    request.Latitude,
                    request.Longitude);
                if (error is not null || visit is null)
                {
                    return error ?? Results.NotFound();
                }

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
                var (error, visit) = await CoverageVisits.TryRecordAsync(
                    principal,
                    request.VehicleId,
                    stopId,
                    request.Skipped,
                    db,
                    bus,
                    gov,
                    ct,
                    request.Latitude,
                    request.Longitude);
                if (error is not null || visit is null)
                {
                    return error ?? Results.NotFound();
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
