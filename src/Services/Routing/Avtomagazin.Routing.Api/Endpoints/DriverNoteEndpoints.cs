using System.Security.Claims;
using Avtomagazin.Contracts;
using Avtomagazin.Contracts.Events;
using Avtomagazin.Routing.Api.Data;
using Avtomagazin.ServiceDefaults;
using MassTransit;
using Microsoft.EntityFrameworkCore;

namespace Avtomagazin.Routing.Api.Endpoints;

public static class DriverNoteEndpoints
{
    public static RouteGroupBuilder MapDriverNoteEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api").WithTags("Routing");

        group.MapGet("/driver-notes", async (RoutingDbContext db) =>
            {
                var cutoff = DateTimeOffset.UtcNow.AddHours(-8);
                var items = await db.DriverNotes.AsNoTracking()
                    .Where(n => n.CreatedAtUtc >= cutoff)
                    .OrderByDescending(n => n.CreatedAtUtc)
                    .ToListAsync();
                return Results.Ok(items.Select(n => n.ToDto()));
            })
            .RequireAuthorization()
            .WithName("ListDriverNotes");

        group.MapPost("/driver-notes", async (
                PostDriverNoteRequest request,
                ClaimsPrincipal principal,
                RoutingDbContext db,
                IPublishEndpoint bus) =>
            {
                if (HttpAccess.ForbidVehicleWrite(principal, request.VehicleId) is { } denied)
                {
                    return denied;
                }

                var body = (request.Body ?? "").Trim();
                if (body.Length == 0)
                {
                    return Results.BadRequest(new { error = "body is required" });
                }

                if (body.Length > 500)
                {
                    body = body[..500];
                }

                var lat = request.Latitude;
                var lng = request.Longitude;
                if (lat is null || lng is null)
                {
                    return Results.BadRequest(new { error = "latitude and longitude are required" });
                }

                var previous = await db.DriverNotes.Where(n => n.VehicleId == request.VehicleId).ToListAsync();
                if (previous.Count > 0 && Roles.IsVanCrew(principal.Role() ?? ""))
                {
                    return Results.Conflict(new { error = "Сначала снимите текущее сообщение" });
                }

                if (previous.Count > 0)
                {
                    db.DriverNotes.RemoveRange(previous);
                }

                var note = new DriverStatusNote
                {
                    Id = Guid.NewGuid(),
                    VehicleId = request.VehicleId,
                    Body = body,
                    Latitude = lat.Value,
                    Longitude = lng.Value,
                    CreatedAtUtc = DateTimeOffset.UtcNow
                };
                db.DriverNotes.Add(note);
                await db.SaveChangesAsync();

                await bus.Publish(new DriverStatusPosted(
                    note.VehicleId,
                    note.Body,
                    note.Latitude,
                    note.Longitude,
                    note.CreatedAtUtc));

                return Results.Ok(note.ToDto());
            })
            .RequireAuthorization(policy => policy.RequireRole(Roles.Driver, Roles.Seller, Roles.Operator, Roles.Admin))
            .WithName("PostDriverNote");

        group.MapDelete("/driver-notes/{vehicleId:guid}", async (
                Guid vehicleId,
                ClaimsPrincipal principal,
                RoutingDbContext db) =>
            {
                if (HttpAccess.ForbidVehicleWrite(principal, vehicleId) is { } denied)
                {
                    return denied;
                }

                var existing = await db.DriverNotes.Where(n => n.VehicleId == vehicleId).ToListAsync();
                if (existing.Count > 0)
                {
                    db.DriverNotes.RemoveRange(existing);
                    await db.SaveChangesAsync();
                }

                return Results.NoContent();
            })
            .RequireAuthorization(policy => policy.RequireRole(Roles.Driver, Roles.Seller, Roles.Operator, Roles.Admin))
            .WithName("ClearDriverNote");

        return group;
    }
}
