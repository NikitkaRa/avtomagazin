using Microsoft.EntityFrameworkCore;

namespace Avtomagazin.Routing.Api.Data;

public sealed class RoutingDbContext(DbContextOptions<RoutingDbContext> options) : DbContext(options)
{
    public DbSet<TradeRoute> Routes => Set<TradeRoute>();
    public DbSet<RouteStop> Stops => Set<RouteStop>();
    public DbSet<EtaSnapshot> EtaSnapshots => Set<EtaSnapshot>();
    public DbSet<CoverageVisit> CoverageVisits => Set<CoverageVisit>();
    public DbSet<StopPresenceReport> PresenceReports => Set<StopPresenceReport>();
    public DbSet<SettlementCase> Cases => Set<SettlementCase>();
    public DbSet<CaseEvent> CaseEvents => Set<CaseEvent>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<TradeRoute>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).HasMaxLength(200);
            e.HasMany(x => x.Stops).WithOne().HasForeignKey(x => x.RouteId);
        });

        modelBuilder.Entity<RouteStop>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.SettlementName).HasMaxLength(200);
            e.Property(x => x.RegionCode).HasMaxLength(16);
            e.HasIndex(x => x.SettlementName);
        });

        modelBuilder.Entity<EtaSnapshot>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.StopId, x.EstimatedArrivalUtc });
        });

        modelBuilder.Entity<CoverageVisit>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.RegionCode, x.ArrivedAtUtc });
        });

        modelBuilder.Entity<StopPresenceReport>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.StopId, x.ReportedAtUtc });
            e.Property(x => x.Kind).HasMaxLength(32);
            e.Property(x => x.DeviceToken).HasMaxLength(512);
            e.Property(x => x.SettlementName).HasMaxLength(200);
        });

        modelBuilder.Entity<SettlementCase>(e =>
        {
            e.ToTable("Cases");
            e.HasKey(x => x.Id);
            e.Property(x => x.SettlementKey).HasMaxLength(120);
            e.Property(x => x.SettlementName).HasMaxLength(200);
            e.Property(x => x.Status).HasMaxLength(32);
            e.HasIndex(x => new { x.SettlementKey, x.Status });
            e.HasMany(x => x.Events).WithOne().HasForeignKey(x => x.CaseId);
        });

        modelBuilder.Entity<CaseEvent>(e =>
        {
            e.ToTable("CaseEvents");
            e.HasKey(x => x.Id);
            e.Property(x => x.Kind).HasMaxLength(32);
            e.Property(x => x.Body).HasMaxLength(2000);
            e.Property(x => x.AuthorName).HasMaxLength(200);
            e.Property(x => x.AuthorEmail).HasMaxLength(320);
            e.Property(x => x.StopLabel).HasMaxLength(200);
            e.Property(x => x.FromStatus).HasMaxLength(32);
            e.Property(x => x.ToStatus).HasMaxLength(32);
            e.HasIndex(x => new { x.CaseId, x.CreatedAtUtc });
        });
    }
}

public sealed class TradeRoute
{
    public Guid Id { get; set; }
    public required string Name { get; set; }
    public Guid VehicleId { get; set; }
    public List<RouteStop> Stops { get; set; } = [];
}

public sealed class RouteStop
{
    public Guid Id { get; set; }
    public Guid RouteId { get; set; }
    public int Sequence { get; set; }
    public required string SettlementName { get; set; }
    public required string RegionCode { get; set; }
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public DateTimeOffset PlannedArrivalUtc { get; set; }
}

public sealed class EtaSnapshot
{
    public Guid Id { get; set; }
    public Guid VehicleId { get; set; }
    public Guid RouteId { get; set; }
    public Guid StopId { get; set; }
    public required string SettlementName { get; set; }
    public DateTimeOffset EstimatedArrivalUtc { get; set; }
    public int MinutesUntilArrival { get; set; }
    public DateTimeOffset CalculatedAtUtc { get; set; }
}

public sealed class CoverageVisit
{
    public Guid Id { get; set; }
    public Guid VehicleId { get; set; }
    public Guid StopId { get; set; }
    public required string SettlementName { get; set; }
    public required string RegionCode { get; set; }
    public DateTimeOffset ArrivedAtUtc { get; set; }
    public bool WithinScheduledWindow { get; set; }
}

public sealed class StopPresenceReport
{
    public Guid Id { get; set; }
    public Guid StopId { get; set; }
    public required string SettlementName { get; set; }
    public required string Kind { get; set; }
    public required string DeviceToken { get; set; }
    public DateTimeOffset ReportedAtUtc { get; set; }
}

public sealed class SettlementCase
{
    public Guid Id { get; set; }
    public required string SettlementKey { get; set; }
    public required string SettlementName { get; set; }
    public Guid? VehicleId { get; set; }
    public required string Status { get; set; }
    public int ReportCount { get; set; }
    public DateTimeOffset OpenedAtUtc { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
    public DateTimeOffset? ClosedAtUtc { get; set; }
    public List<CaseEvent> Events { get; set; } = [];
}

public sealed class CaseEvent
{
    public Guid Id { get; set; }
    public Guid CaseId { get; set; }
    public required string Kind { get; set; }
    public required string Body { get; set; }
    public string? AuthorName { get; set; }
    public string? AuthorEmail { get; set; }
    public Guid? StopId { get; set; }
    public string? StopLabel { get; set; }
    public string? FromStatus { get; set; }
    public string? ToStatus { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
}

public static class SettlementNames
{
    public static string Key(string? raw)
    {
        var name = (raw ?? "").Trim();
        if (name.Length == 0)
        {
            return "unknown";
        }

        var cut = name.IndexOf(" · ", StringComparison.Ordinal);
        if (cut < 0)
        {
            cut = name.IndexOf(" - ", StringComparison.Ordinal);
        }

        return cut > 0 ? name[..cut].Trim() : name;
    }
}

public static class Seed
{
    public static async Task EnsureSeedAsync(RoutingDbContext db)
    {
        if (!await db.Routes.AnyAsync())
        {
            var routeId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
            var now = DateTimeOffset.UtcNow;

            var route = new TradeRoute
            {
                Id = routeId,
                Name = "Гродно — окраинные деревни",
                VehicleId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
                Stops =
                [
                    new RouteStop
                    {
                        Id = Guid.Parse("dddddddd-dddd-dddd-dddd-ddddddddddd1"),
                        RouteId = routeId,
                        Sequence = 1,
                        SettlementName = "Индура",
                        RegionCode = "BY-HR",
                        Latitude = 53.4600,
                        Longitude = 23.9500,
                        PlannedArrivalUtc = now.AddMinutes(10)
                    },
                    new RouteStop
                    {
                        Id = Guid.Parse("dddddddd-dddd-dddd-dddd-ddddddddddd2"),
                        RouteId = routeId,
                        Sequence = 2,
                        SettlementName = "Озёры",
                        RegionCode = "BY-HR",
                        Latitude = 53.7200,
                        Longitude = 24.1800,
                        PlannedArrivalUtc = now.AddHours(2)
                    },
                    new RouteStop
                    {
                        Id = Guid.Parse("dddddddd-dddd-dddd-dddd-ddddddddddd3"),
                        RouteId = routeId,
                        Sequence = 3,
                        SettlementName = "Скидель",
                        RegionCode = "BY-HR",
                        Latitude = 53.5900,
                        Longitude = 24.2500,
                        PlannedArrivalUtc = now.AddHours(3)
                    }
                ]
            };

            db.Routes.Add(route);
            await db.SaveChangesAsync();
        }

        await EnsurePukhovichiRouteAsync(db);
    }

    private static async Task EnsurePukhovichiRouteAsync(RoutingDbContext db)
    {
        var routeId = Guid.Parse("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee");
        var vehicleId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
        var now = DateTimeOffset.UtcNow;

        var route = await db.Routes.Include(r => r.Stops).FirstOrDefaultAsync(r => r.Id == routeId);
        if (route is null)
        {
            route = new TradeRoute
            {
                Id = routeId,
                Name = "Пуховичи — Озеричино",
                VehicleId = vehicleId
            };
            db.Routes.Add(route);
            await db.SaveChangesAsync();
        }
        else if (route.VehicleId != vehicleId || route.Name != "Пуховичи — Озеричино")
        {
            route.VehicleId = vehicleId;
            route.Name = "Пуховичи — Озеричино";
        }

        await UpsertStopAsync(db, routeId, Guid.Parse("dddddddd-dddd-dddd-dddd-ddddddddddd5"), 1,
            "Правдинский", "BY-MI", 53.5085, 27.8372, now.AddMinutes(25));
        await UpsertStopAsync(db, routeId, Guid.Parse("dddddddd-dddd-dddd-dddd-ddddddddddd4"), 2,
            "Озеричино", "BY-MI", 53.5774, 27.7472, now.AddMinutes(8));
        await UpsertStopAsync(db, routeId, Guid.Parse("dddddddd-dddd-dddd-dddd-ddddddddddd6"), 3,
            "Дукора", "BY-MI", 53.6786, 27.9525, now.AddMinutes(55));
        await db.SaveChangesAsync();
    }

    private static async Task UpsertStopAsync(
        RoutingDbContext db,
        Guid routeId,
        Guid stopId,
        int sequence,
        string settlement,
        string region,
        double lat,
        double lng,
        DateTimeOffset plannedUtc)
    {
        var stop = await db.Stops.FirstOrDefaultAsync(s => s.Id == stopId)
                   ?? await db.Stops.FirstOrDefaultAsync(s => s.SettlementName == settlement);
        if (stop is null)
        {
            db.Stops.Add(new RouteStop
            {
                Id = stopId,
                RouteId = routeId,
                Sequence = sequence,
                SettlementName = settlement,
                RegionCode = region,
                Latitude = lat,
                Longitude = lng,
                PlannedArrivalUtc = plannedUtc
            });
            return;
        }

        stop.RouteId = routeId;
        stop.Sequence = sequence;
        stop.SettlementName = settlement;
        stop.RegionCode = region;
        stop.Latitude = lat;
        stop.Longitude = lng;
        stop.PlannedArrivalUtc = plannedUtc;
    }
}
