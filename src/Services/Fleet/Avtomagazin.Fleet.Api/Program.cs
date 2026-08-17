using System.Security.Claims;
using Avtomagazin.Contracts;
using Avtomagazin.Contracts.Events;
using Avtomagazin.Fleet.Api;
using Avtomagazin.Fleet.Api.Consumers;
using Avtomagazin.Fleet.Api.Data;
using Avtomagazin.Fleet.Api.Gps;
using Avtomagazin.ServiceDefaults;
using MassTransit;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);
builder.AddAvtomagazinDefaults(bus => bus.AddConsumer<StaffContactChangedConsumer>());
builder.AddAvtomagazinObjectStorage();

var fleetCs = DeploySecrets.ConnectionString(
    builder.Configuration,
    builder.Environment,
    "Fleet",
    "Host=localhost;Port=5432;Database=avtomagazin_fleet;Username=avtomagazin;Password=avtomagazin");

var fleetTestDb = $"avtomagazin-fleet-tests-{Guid.NewGuid()}";
builder.Services.AddDbContext<FleetDbContext>(options =>
{
    if (builder.Environment.IsEnvironment("Testing"))
    {
        options.UseInMemoryDatabase(fleetTestDb);
        return;
    }

    options.UseNpgsql(fleetCs);
});

if (!builder.Environment.IsEnvironment("Testing"))
{
    builder.AddAvtomagazinPostgresHealth("postgres", fleetCs);
}

var gpsProvider = builder.Configuration["Gps:Provider"]
                  ?? (builder.Environment.IsDevelopment() ? "Mock" : "None");
if (string.Equals(gpsProvider, "Mock", StringComparison.OrdinalIgnoreCase))
{
    builder.Services.AddSingleton<IGpsProvider, MockGpsProvider>();
    builder.Services.AddHostedService<GpsPollingWorker>();
}

var app = builder.Build();
app.UseAvtomagazinDefaults();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<FleetDbContext>();
    await RelationalSchema.ApplyAsync(db);
    var seedDemo = app.Configuration.GetValue(
        "Seed:DemoData",
        app.Environment.IsDevelopment() || app.Environment.IsEnvironment("Testing"));
    if (seedDemo)
    {
        await Seed.EnsureSeedAsync(db);
    }
}

app.MapGet("/api/vehicles", async (FleetDbContext db) =>
    await db.Vehicles.AsNoTracking().OrderBy(v => v.PlateNumber).ToListAsync())
    .RequireAuthorization()
    .WithName("ListVehicles")
    .WithTags("Fleet");

app.MapGet("/api/vehicles/{id:guid}", async (Guid id, FleetDbContext db) =>
{
    var vehicle = await db.Vehicles.AsNoTracking().FirstOrDefaultAsync(v => v.Id == id);
    return vehicle is null ? Results.NotFound() : Results.Ok(vehicle);
})
.RequireAuthorization()
.WithName("GetVehicle")
.WithTags("Fleet");

app.MapGet("/api/vehicles/{id:guid}/position", async (Guid id, FleetDbContext db) =>
{
    var position = await db.Positions.AsNoTracking()
        .Where(p => p.VehicleId == id)
        .OrderByDescending(p => p.RecordedAtUtc)
        .FirstOrDefaultAsync();

    return position is null ? Results.NotFound() : Results.Ok(position);
})
.RequireAuthorization()
.WithName("GetVehiclePosition")
.WithTags("Fleet");

app.MapPost("/api/vehicles", async (UpsertVehicleRequest request, FleetDbContext db, IObjectStorage storage) =>
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
    ApplyCrewSnapshot(vehicle, request);

    db.Vehicles.Add(vehicle);
    await db.SaveChangesAsync();
    return Results.Created($"/api/vehicles/{vehicle.Id}", vehicle);
})
.RequireAuthorization(policy => policy.RequireRole(Roles.Operator, Roles.Admin))
.WithName("CreateVehicle")
.WithTags("Fleet");

app.MapPut("/api/vehicles/{id:guid}", async (Guid id, UpsertVehicleRequest request, FleetDbContext db, IObjectStorage storage) =>
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
    ApplyCrewSnapshot(vehicle, request);
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
.WithName("UpdateVehicle")
.WithTags("Fleet");

app.MapPatch("/api/vehicles/{id:guid}/active", async (Guid id, SetActiveRequest request, FleetDbContext db) =>
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
.WithName("SetVehicleActive")
.WithTags("Fleet");

app.MapPost("/api/vehicles/{id:guid}/positions", async (
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
.WithName("IngestPosition")
.WithTags("Fleet");

app.MapPatch("/api/vehicles/{id:guid}/contacts", async (Guid id, UpdateVehicleContactsRequest request, FleetDbContext db) =>
{
    var vehicle = await db.Vehicles.FirstOrDefaultAsync(v => v.Id == id);
    if (vehicle is null)
    {
        return Results.NotFound();
    }

    // Driver/seller name+phone are snapshots of Identity when UserId is set.
    // Free-text contacts remain only for vehicles without assigned staff users.
    if (vehicle.DriverUserId is null)
    {
        vehicle.DriverName = TrimOrNull(request.DriverName);
        vehicle.DriverPhone = TrimOrNull(request.DriverPhone);
    }

    if (vehicle.SellerUserId is null)
    {
        vehicle.SellerName = TrimOrNull(request.SellerName);
        vehicle.SellerPhone = TrimOrNull(request.SellerPhone);
    }

    vehicle.OperatorPhone = TrimOrNull(request.OperatorPhone);
    await db.SaveChangesAsync();
    return Results.Ok(vehicle);
})
.RequireAuthorization(policy => policy.RequireRole(Roles.Operator, Roles.Admin))
.WithName("UpdateVehicleContacts")
.WithTags("Fleet");

static void ApplyCrewSnapshot(Vehicle vehicle, UpsertVehicleRequest request)
{
    vehicle.DriverUserId = request.DriverUserId;
    if (request.DriverUserId is null)
    {
        vehicle.DriverName = null;
        vehicle.DriverPhone = null;
    }
    else
    {
        // Snapshot at assignment time; kept in sync via StaffContactChanged.
        vehicle.DriverName = VehiclePhotos.TrimOrNull(request.DriverName);
        vehicle.DriverPhone = VehiclePhotos.TrimOrNull(request.DriverPhone);
    }

    vehicle.SellerUserId = request.SellerUserId;
    if (request.SellerUserId is null)
    {
        vehicle.SellerName = null;
        vehicle.SellerPhone = null;
    }
    else
    {
        vehicle.SellerName = VehiclePhotos.TrimOrNull(request.SellerName);
        vehicle.SellerPhone = VehiclePhotos.TrimOrNull(request.SellerPhone);
    }
}

static string? TrimOrNull(string? value)
    => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

app.Run();

public sealed record PositionIngestRequest(
    double Latitude,
    double Longitude,
    double? SpeedKmh,
    DateTimeOffset? RecordedAtUtc,
    string? Source);

public sealed record UpsertVehicleRequest(
    string PlateNumber,
    string OperatorName,
    string? DriverName = null,
    string? DriverPhone = null,
    string? SellerName = null,
    string? SellerPhone = null,
    string? OperatorPhone = null,
    Guid? DriverUserId = null,
    Guid? SellerUserId = null,
    string? PhotoDataUrl = null,
    bool? ClearPhoto = null,
    bool? IsActive = null);

public sealed record CreateVehicleRequest(string PlateNumber, string OperatorName);
public sealed record SetActiveRequest(bool IsActive);
public sealed record UpdateVehicleContactsRequest(
    string? DriverName,
    string? DriverPhone,
    string? SellerName,
    string? SellerPhone,
    string? OperatorPhone);

public partial class Program;
