using Avtomagazin.Fleet.Api.Data;
using Avtomagazin.Fleet.Api.Gps;
using Microsoft.EntityFrameworkCore;

namespace Avtomagazin.UnitTests.Gps;

public class GpsPositionRetentionTests
{
    [Fact]
    public async Task Purge_drops_points_older_than_24h_and_keeps_fresh()
    {
        var vanId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        await using var db = new FleetDbContext(new DbContextOptionsBuilder<FleetDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

        db.Vehicles.Add(new Vehicle { Id = vanId, PlateNumber = "1", OperatorName = "A", IsActive = true });
        db.Positions.AddRange(
            new VehiclePosition
            {
                Id = Guid.NewGuid(),
                VehicleId = vanId,
                Latitude = 53,
                Longitude = 23,
                RecordedAtUtc = DateTimeOffset.UtcNow.AddHours(-25)
            },
            new VehiclePosition
            {
                Id = Guid.NewGuid(),
                VehicleId = vanId,
                Latitude = 53.1,
                Longitude = 23.1,
                RecordedAtUtc = DateTimeOffset.UtcNow.AddHours(-1)
            });
        await db.SaveChangesAsync();

        var removed = await GpsPositionRetention.PurgeAsync(db);

        Assert.Equal(1, removed);
        Assert.Equal(1, await db.Positions.CountAsync());
        var kept = await db.Positions.SingleAsync();
        Assert.True(DateTimeOffset.UtcNow - kept.RecordedAtUtc < GpsPositionRetention.KeepFor);
    }
}
