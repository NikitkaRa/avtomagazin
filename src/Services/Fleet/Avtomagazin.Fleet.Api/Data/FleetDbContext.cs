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
            e.Property(x => x.DriverName).HasMaxLength(120);
            e.Property(x => x.DriverPhone).HasMaxLength(32);
            e.Property(x => x.SellerName).HasMaxLength(120);
            e.Property(x => x.SellerPhone).HasMaxLength(32);
            e.Property(x => x.OperatorPhone).HasMaxLength(32);
            e.Property(x => x.PhotoDataUrl);
            e.Property(x => x.DriverUserId);
            e.Property(x => x.SellerUserId);
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
    public string? DriverName { get; set; }
    public string? DriverPhone { get; set; }
    public string? SellerName { get; set; }
    public string? SellerPhone { get; set; }
    public string? OperatorPhone { get; set; }
    public Guid? DriverUserId { get; set; }
    public Guid? SellerUserId { get; set; }
    public string? PhotoDataUrl { get; set; }
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
            23.8131,
            "Иван Петров",
            "+375291110011",
            "Анна Коваль",
            "+375291110012",
            "+375152600100");
        await UpsertVehicleAsync(
            db,
            Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
            "5678 CD-4",
            "Пуховичское райпо",
            53.5774,
            27.7472,
            "Сергей Новик",
            "+375297770021",
            "Мария Савич",
            "+375297770022",
            "+375171600200");
        await db.SaveChangesAsync();
    }

    private static async Task UpsertVehicleAsync(
        FleetDbContext db,
        Guid id,
        string plate,
        string operatorName,
        double lat,
        double lng,
        string driverName,
        string driverPhone,
        string sellerName,
        string sellerPhone,
        string operatorPhone)
    {
        var vehicle = await db.Vehicles.FirstOrDefaultAsync(v => v.Id == id);
        if (vehicle is null)
        {
            db.Vehicles.Add(new Vehicle
            {
                Id = id,
                PlateNumber = plate,
                OperatorName = operatorName,
                DriverName = driverName,
                DriverPhone = driverPhone,
                SellerName = sellerName,
                SellerPhone = sellerPhone,
                OperatorPhone = operatorPhone,
                LastLatitude = lat,
                LastLongitude = lng,
                LastSeenAtUtc = DateTimeOffset.UtcNow
            });
            return;
        }

        vehicle.PlateNumber = plate;
        vehicle.OperatorName = operatorName;
        vehicle.DriverName = driverName;
        vehicle.DriverPhone = driverPhone;
        vehicle.SellerName = sellerName;
        vehicle.SellerPhone = sellerPhone;
        vehicle.OperatorPhone = operatorPhone;
        vehicle.LastLatitude ??= lat;
        vehicle.LastLongitude ??= lng;
    }
}
