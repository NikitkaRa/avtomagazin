using System.Security.Claims;
using Avtomagazin.Contracts;
using Avtomagazin.Contracts.Events;
using Avtomagazin.Fleet.Api.Data;
using Avtomagazin.ServiceDefaults;
using MassTransit;
using Microsoft.EntityFrameworkCore;

namespace Avtomagazin.Fleet.Api.Endpoints;

public static class VehicleEndpoints
{
    public static RouteGroupBuilder MapVehicleEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api").WithTags("Fleet");

        group.MapGet("/vehicles", async (FleetDbContext db) =>
                await db.Vehicles.AsNoTracking().OrderBy(v => v.PlateNumber).ToListAsync())
            .RequireAuthorization()
            .WithName("ListVehicles");

        group.MapGet("/vehicles/{id:guid}", async (Guid id, FleetDbContext db) =>
            {
                var vehicle = await db.Vehicles.AsNoTracking().FirstOrDefaultAsync(v => v.Id == id);
                return vehicle is null ? Results.NotFound() : Results.Ok(vehicle);
            })
            .RequireAuthorization()
            .WithName("GetVehicle");

        group.MapGet("/vehicles/{id:guid}/position", async (Guid id, FleetDbContext db) =>
            {
                var position = await db.Positions.AsNoTracking()
                    .Where(p => p.VehicleId == id)
                    .OrderByDescending(p => p.RecordedAtUtc)
                    .FirstOrDefaultAsync();
                return position is null ? Results.NotFound() : Results.Ok(position);
            })
            .RequireAuthorization()
            .WithName("GetVehiclePosition");

        group.MapPost("/vehicles", async (UpsertVehicleRequest request, FleetDbContext db, IObjectStorage storage) =>
            {
                if (!PlateNumbers.TryNormalize(request.PlateNumber, out var plate, out var plateError))
                {
                    return Results.BadRequest(new { error = plateError });
                }

                if (string.IsNullOrWhiteSpace(request.OperatorName))
                {
                    return Results.BadRequest(new { error = "Укажите оператора (райпо)" });
                }

                var (photo, photoError) = await MediaPhotos.StoreAsync(
                    storage,
                    "vehicles",
                    request.PhotoDataUrl,
                    previousUrl: null,
                    clear: request.ClearPhoto == true);
                if (photoError is not null)
                {
                    return Results.BadRequest(new { error = photoError });
                }

                if (await db.Vehicles.AnyAsync(v => v.PlateNumber == plate))
                {
                    return Results.Conflict(new { error = "Автолавка с таким номером уже есть" });
                }

                var vehicle = new Vehicle
                {
                    Id = Guid.NewGuid(),
                    PlateNumber = plate,
                    OperatorName = request.OperatorName.Trim(),
                    OperatorPhone = VehiclePhotos.TrimOrNull(request.OperatorPhone),
                    PhotoDataUrl = photo,
                    IsActive = request.IsActive ?? true
                };
                VehicleCrew.ApplySnapshot(vehicle, request);

                db.Vehicles.Add(vehicle);
                await db.SaveChangesAsync();
                return Results.Created($"/api/vehicles/{vehicle.Id}", vehicle);
            })
            .RequireAuthorization(policy => policy.RequireRole(Roles.Operator, Roles.Admin))
            .WithName("CreateVehicle");

        group.MapPut("/vehicles/{id:guid}", async (Guid id, UpsertVehicleRequest request, FleetDbContext db, IObjectStorage storage) =>
            {
                var vehicle = await db.Vehicles.FirstOrDefaultAsync(v => v.Id == id);
                if (vehicle is null)
                {
                    return Results.NotFound();
                }

                if (!PlateNumbers.TryNormalize(request.PlateNumber, out var plate, out var plateError))
                {
                    return Results.BadRequest(new { error = plateError });
                }

                if (string.IsNullOrWhiteSpace(request.OperatorName))
                {
                    return Results.BadRequest(new { error = "Укажите оператора (райпо)" });
                }

                var (photo, photoError) = await MediaPhotos.StoreAsync(
                    storage,
                    "vehicles",
                    request.ClearPhoto == true ? null : request.PhotoDataUrl,
                    vehicle.PhotoDataUrl,
                    clear: request.ClearPhoto == true);
                if (photoError is not null)
                {
                    return Results.BadRequest(new { error = photoError });
                }

                if (await db.Vehicles.AnyAsync(v => v.PlateNumber == plate && v.Id != id))
                {
                    return Results.Conflict(new { error = "Автолавка с таким номером уже есть" });
                }

                vehicle.PlateNumber = plate;
                vehicle.OperatorName = request.OperatorName.Trim();
                VehicleCrew.ApplySnapshot(vehicle, request);
                vehicle.OperatorPhone = VehiclePhotos.TrimOrNull(request.OperatorPhone);
                if (request.ClearPhoto == true || request.PhotoDataUrl is not null)
                {
                    vehicle.PhotoDataUrl = photo;
                }

                if (request.IsActive is bool active)
                {
                    vehicle.IsActive = active;
                }

                await db.SaveChangesAsync();
                return Results.Ok(vehicle);
            })
            .RequireAuthorization(policy => policy.RequireRole(Roles.Operator, Roles.Admin))
            .WithName("UpdateVehicle");

        group.MapPatch("/vehicles/{id:guid}/active", async (Guid id, SetActiveRequest request, FleetDbContext db) =>
            {
                var vehicle = await db.Vehicles.FirstOrDefaultAsync(v => v.Id == id);
                if (vehicle is null)
                {
                    return Results.NotFound();
                }

                vehicle.IsActive = request.IsActive;
                await db.SaveChangesAsync();
                return Results.Ok(vehicle);
            })
            .RequireAuthorization(policy => policy.RequireRole(Roles.Operator, Roles.Admin))
            .WithName("SetVehicleActive");

        group.MapPost("/vehicles/{id:guid}/positions", async (
                Guid id,
                PositionIngestRequest request,
                ClaimsPrincipal principal,
                FleetDbContext db,
                IPublishEndpoint bus) =>
            {
                if (HttpAccess.ForbidVehicleWrite(principal, id) is { } denied)
                {
                    return denied;
                }

                var vehicle = await db.Vehicles.FirstOrDefaultAsync(v => v.Id == id);
                if (vehicle is null)
                {
                    return Results.NotFound();
                }

                var recordedAt = request.RecordedAtUtc ?? DateTimeOffset.UtcNow;
                var position = new VehiclePosition
                {
                    Id = Guid.NewGuid(),
                    VehicleId = id,
                    Latitude = request.Latitude,
                    Longitude = request.Longitude,
                    SpeedKmh = request.SpeedKmh,
                    RecordedAtUtc = recordedAt,
                    Source = request.Source ?? GpsSources.Manual
                };

                vehicle.LastLatitude = position.Latitude;
                vehicle.LastLongitude = position.Longitude;
                vehicle.LastSeenAtUtc = recordedAt;
                vehicle.LastSource = position.Source;

                db.Positions.Add(position);
                await db.SaveChangesAsync();

                await bus.Publish(new VehiclePositionUpdated(
                    vehicle.Id,
                    vehicle.PlateNumber,
                    position.Latitude,
                    position.Longitude,
                    position.SpeedKmh,
                    position.RecordedAtUtc));

                return Results.Created($"/api/vehicles/{id}/position", position);
            })
            .RequireAuthorization(policy => policy.RequireRole(Roles.Driver, Roles.Seller, Roles.Operator, Roles.Admin))
            .WithName("IngestPosition");

        group.MapPatch("/vehicles/{id:guid}/contacts", async (Guid id, UpdateVehicleContactsRequest request, FleetDbContext db) =>
            {
                var vehicle = await db.Vehicles.FirstOrDefaultAsync(v => v.Id == id);
                if (vehicle is null)
                {
                    return Results.NotFound();
                }

                if (vehicle.DriverUserId is null)
                {
                    vehicle.DriverName = VehiclePhotos.TrimOrNull(request.DriverName);
                    vehicle.DriverPhone = VehiclePhotos.TrimOrNull(request.DriverPhone);
                }

                if (vehicle.SellerUserId is null)
                {
                    vehicle.SellerName = VehiclePhotos.TrimOrNull(request.SellerName);
                    vehicle.SellerPhone = VehiclePhotos.TrimOrNull(request.SellerPhone);
                }

                vehicle.OperatorPhone = VehiclePhotos.TrimOrNull(request.OperatorPhone);
                await db.SaveChangesAsync();
                return Results.Ok(vehicle);
            })
            .RequireAuthorization(policy => policy.RequireRole(Roles.Operator, Roles.Admin))
            .WithName("UpdateVehicleContacts");

        return group;
    }
}
