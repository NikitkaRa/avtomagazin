using Microsoft.EntityFrameworkCore;

namespace Avtomagazin.Fleet.Api.Data;

public sealed class FleetDbContext(DbContextOptions<FleetDbContext> options) : DbContext(options)
{
    public DbSet<Vehicle> Vehicles => Set<Vehicle>();
    public DbSet<VehiclePosition> Positions => Set<VehiclePosition>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Vehicle>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.PlateNumber).IsUnique();
            e.Property(x => x.PlateNumber).HasMaxLength(32);
            e.Property(x => x.OperatorName).HasMaxLength(200);
            e.Property(x => x.LastSource).HasMaxLength(64);
        });

        modelBuilder.Entity<VehiclePosition>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.VehicleId, x.RecordedAtUtc });
            e.Property(x => x.Source).HasMaxLength(64);
        });
    }
}

public sealed class Vehicle
{
    public Guid Id { get; set; }
    public required string PlateNumber { get; set; }
    public required string OperatorName { get; set; }
    public bool IsActive { get; set; } = true;
    public double? LastLatitude { get; set; }
    public double? LastLongitude { get; set; }
    public DateTimeOffset? LastSeenAtUtc { get; set; }
    public string? LastSource { get; set; }
}

public sealed class VehiclePosition
{
    public Guid Id { get; set; }
    public Guid VehicleId { get; set; }
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public double? SpeedKmh { get; set; }
    public DateTimeOffset RecordedAtUtc { get; set; }
    public string Source { get; set; } = "gps";
}

public static class Seed
{
    public static async Task EnsureSeedAsync(FleetDbContext db)
    {
        await UpsertVehicleAsync(
            db,
            Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
            "1234 AB-7",
            "Гродненское райпо",
            53.6694,
            23.8131);
        await UpsertVehicleAsync(
            db,
            Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
            "5678 CD-4",
            "Пуховичское райпо",
            53.5774,
            27.7472);
        await db.SaveChangesAsync();
    }

    private static async Task UpsertVehicleAsync(
        FleetDbContext db,
        Guid id,
        string plate,
        string operatorName,
        double lat,
        double lng)
    {
        var vehicle = await db.Vehicles.FirstOrDefaultAsync(v => v.Id == id);
        if (vehicle is null)
        {
            db.Vehicles.Add(new Vehicle
            {
                Id = id,
                PlateNumber = plate,
                OperatorName = operatorName,
                LastLatitude = lat,
                LastLongitude = lng,
                LastSeenAtUtc = DateTimeOffset.UtcNow
            });
            return;
        }

        vehicle.PlateNumber = plate;
        vehicle.OperatorName = operatorName;
        vehicle.LastLatitude ??= lat;
        vehicle.LastLongitude ??= lng;
    }
}
