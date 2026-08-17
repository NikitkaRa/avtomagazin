using Avtomagazin.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Avtomagazin.Routing.Api.Data;

public sealed class RoutingDbContext(DbContextOptions<RoutingDbContext> options) : DbContext(options)
{
    public DbSet<TradeRoute> Routes => Set<TradeRoute>();
    public DbSet<RouteStop> Stops => Set<RouteStop>();
    public DbSet<DriverStatusNote> DriverNotes => Set<DriverStatusNote>();
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
            e.Property(x => x.IsCatalog).HasDefaultValue(false);
            e.HasMany(x => x.Stops).WithOne().HasForeignKey(x => x.RouteId);
        });

        modelBuilder.Entity<RouteStop>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.SettlementName).HasMaxLength(200);
            e.Property(x => x.RegionCode).HasMaxLength(16);
            e.Property(x => x.PhotoDataUrl);
            e.HasIndex(x => x.SettlementName);
        });

        modelBuilder.Entity<DriverStatusNote>(e =>
        {
            e.ToTable("DriverNotes");
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.VehicleId).IsUnique();
            e.Property(x => x.Body).HasMaxLength(500);
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
    public bool IsCatalog { get; set; }
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
    public string? PhotoDataUrl { get; set; }
}

/// <summary>Latest roadside note from the van crew — one active row per vehicle.</summary>
public sealed class DriverStatusNote
{
    public Guid Id { get; set; }
    public Guid VehicleId { get; set; }
    public required string Body { get; set; }
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
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
    public bool Skipped { get; set; }
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
        await EnsureGrodnoRouteAsync(db);
        await EnsurePukhovichiRouteAsync(db);
        await EnsureOzerichinoLoopRoutesAsync(db);
        await EnsureBelarusHeatRouteAsync(db);
    }

    /// <summary>
    /// Demo driver (Гродно) trip — timings match ~30 km/h from the seeded van near Grodno.
    /// Existing rows are left alone so a restart cannot teleport stops or rewind the day.
    /// </summary>
    private static async Task EnsureGrodnoRouteAsync(RoutingDbContext db)
    {
        var routeId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
        if (await db.Routes.AnyAsync(r => r.Id == routeId))
        {
            return;
        }

        var now = DateTimeOffset.UtcNow;
        db.Routes.Add(new TradeRoute
        {
            Id = routeId,
            Name = "Гродно — окраинные деревни",
            VehicleId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa")
        });
        InsertStop(db, routeId, Guid.Parse("dddddddd-dddd-dddd-dddd-ddddddddddd1"), 1,
            "Индура", "BY-HR", 53.4600, 23.9500, now.AddMinutes(45));
        InsertStop(db, routeId, Guid.Parse("dddddddd-dddd-dddd-dddd-ddddddddddd2"), 2,
            "Озёры", "BY-HR", 53.7200, 24.1800, now.AddMinutes(100));
        InsertStop(db, routeId, Guid.Parse("dddddddd-dddd-dddd-dddd-ddddddddddd3"), 3,
            "Скидель", "BY-HR", 53.5900, 24.2500, now.AddMinutes(145));
        await db.SaveChangesAsync();
    }

    private static async Task EnsureBelarusHeatRouteAsync(RoutingDbContext db)
    {
        var routeId = DemoHeatCatalog.HeatRouteId;
        var existing = await db.Routes.FirstOrDefaultAsync(r => r.Id == routeId);
        if (existing is not null)
        {
            if (!existing.IsCatalog)
            {
                existing.IsCatalog = true;
                await db.SaveChangesAsync();
            }

            return;
        }

        db.Routes.Add(new TradeRoute
        {
            Id = routeId,
            Name = "Беларусь — избранное",
            VehicleId = DemoHeatCatalog.HeatVehicleId,
            IsCatalog = true
        });

        var now = DateTimeOffset.UtcNow;
        var places = DemoHeatCatalog.Places;
        for (var i = 0; i < places.Count; i++)
        {
            var place = places[i];
            var jitterLat = ((i % 7) - 3) * 0.012;
            var jitterLng = ((i % 5) - 2) * 0.015;
            InsertStop(
                db,
                routeId,
                DemoHeatCatalog.StopId(i),
                i + 1,
                place.Name,
                place.Region,
                place.Lat + jitterLat,
                place.Lng + jitterLng,
                now.AddMinutes(20 + i * 12));
        }

        await db.SaveChangesAsync();
    }

    private static async Task EnsurePukhovichiRouteAsync(RoutingDbContext db)
    {
        var routeId = Guid.Parse("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee");
        if (await db.Routes.AnyAsync(r => r.Id == routeId))
        {
            return;
        }

        var now = DateTimeOffset.UtcNow;
        db.Routes.Add(new TradeRoute
        {
            Id = routeId,
            Name = "Пуховичи — Озеричино",
            VehicleId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb")
        });
        InsertStop(db, routeId, Guid.Parse("dddddddd-dddd-dddd-dddd-ddddddddddd4"), 1,
            "Озеричино", "BY-MI", 53.5774, 27.7472, now.AddMinutes(15));
        InsertStop(db, routeId, Guid.Parse("dddddddd-dddd-dddd-dddd-ddddddddddd5"), 2,
            "Правдинский", "BY-MI", 53.5085, 27.8372, now.AddMinutes(50));
        InsertStop(db, routeId, Guid.Parse("dddddddd-dddd-dddd-dddd-ddddddddddd6"), 3,
            "Дукора", "BY-MI", 53.6786, 27.9525, now.AddMinutes(95));
        await db.SaveChangesAsync();
    }

    /// <summary>Extra operational trips around Озеричино / Пуховичи so dispatch is not a two-row list.</summary>
    private static async Task EnsureOzerichinoLoopRoutesAsync(RoutingDbContext db)
    {
        var now = DateTimeOffset.UtcNow;
        await EnsureLoopRouteAsync(
            db,
            Guid.Parse("f2000000-0000-4000-8000-000000000011"),
            Guid.Parse("f2000000-0000-4000-8000-000000000001"),
            "Озеричино — Марьина Горка",
            [
                new LoopStop(Guid.Parse("f2000000-0000-4000-8000-000000000101"), 1, "Пуховичи", 53.5294, 28.2467, now.AddMinutes(-55), Arrived: true),
                new LoopStop(Guid.Parse("f2000000-0000-4000-8000-000000000102"), 2, "Дружный", 53.6238, 27.8874, now.AddMinutes(18), Arrived: false),
                new LoopStop(Guid.Parse("f2000000-0000-4000-8000-000000000103"), 3, "Марьина Горка", 53.5042, 28.1556, now.AddMinutes(70), Arrived: false),
                new LoopStop(Guid.Parse("f2000000-0000-4000-8000-000000000104"), 4, "Шацк", 53.3688, 27.8472, now.AddMinutes(125), Arrived: false)
            ]);
        await EnsureLoopRouteAsync(
            db,
            Guid.Parse("f2000000-0000-4000-8000-000000000012"),
            Guid.Parse("f2000000-0000-4000-8000-000000000002"),
            "Озеричино — Свислочь",
            [
                new LoopStop(Guid.Parse("f2000000-0000-4000-8000-000000000201"), 1, "Руденск", 53.5946, 27.8608, now.AddMinutes(-80), Arrived: true),
                new LoopStop(Guid.Parse("f2000000-0000-4000-8000-000000000202"), 2, "Зазерка", 53.5512, 27.8014, now.AddMinutes(-35), Arrived: true),
                new LoopStop(Guid.Parse("f2000000-0000-4000-8000-000000000203"), 3, "Свислочь", 53.4372, 28.0015, now.AddMinutes(25), Arrived: false),
                new LoopStop(Guid.Parse("f2000000-0000-4000-8000-000000000204"), 4, "Блонь", 53.5218, 28.0836, now.AddMinutes(85), Arrived: false)
            ]);
    }

    private static async Task EnsureLoopRouteAsync(
        RoutingDbContext db,
        Guid routeId,
        Guid vehicleId,
        string name,
        LoopStop[] stops)
    {
        if (await db.Routes.AnyAsync(r => r.Id == routeId))
        {
            return;
        }

        db.Routes.Add(new TradeRoute
        {
            Id = routeId,
            Name = name,
            VehicleId = vehicleId
        });

        foreach (var stop in stops)
        {
            InsertStop(db, routeId, stop.Id, stop.Sequence, stop.Name, "BY-MI", stop.Lat, stop.Lng, stop.PlannedUtc);
            if (stop.Arrived)
            {
                InsertVisit(db, vehicleId, stop.Id, stop.Name, stop.PlannedUtc.AddMinutes(-8));
            }
        }

        await db.SaveChangesAsync();
    }

    private static void InsertVisit(
        RoutingDbContext db,
        Guid vehicleId,
        Guid stopId,
        string settlement,
        DateTimeOffset arrivedAtUtc)
    {
        var visitId = Guid.Parse($"f2100000-0000-4000-8000-{stopId.ToString("N")[^12..]}");
        if (db.CoverageVisits.Find(visitId) is not null)
        {
            return;
        }

        db.CoverageVisits.Add(new CoverageVisit
        {
            Id = visitId,
            VehicleId = vehicleId,
            StopId = stopId,
            SettlementName = settlement,
            RegionCode = "BY-MI",
            ArrivedAtUtc = arrivedAtUtc,
            WithinScheduledWindow = true
        });
    }

    private sealed record LoopStop(
        Guid Id,
        int Sequence,
        string Name,
        double Lat,
        double Lng,
        DateTimeOffset PlannedUtc,
        bool Arrived);

    private static void InsertStop(
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
        if (db.Stops.Find(stopId) is not null)
        {
            return;
        }

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
    }
}
