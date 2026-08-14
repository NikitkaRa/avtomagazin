using Avtomagazin.Fleet.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace Avtomagazin.UnitTests.Fleet;

public class FleetSeedTests
{
    [Fact]
    public async Task EnsureSeed_adds_pukhovichi_van_even_if_grodno_exists()
    {
        var options = new DbContextOptionsBuilder<FleetDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        await using var db = new FleetDbContext(options);
        db.Vehicles.Add(new Vehicle
        {
            Id = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
            PlateNumber = "1234 AB-7",
            OperatorName = "Гродненское райпо"
        });
        await db.SaveChangesAsync();

        await Seed.EnsureSeedAsync(db);

        var plates = await db.Vehicles.AsNoTracking().Select(v => v.PlateNumber).ToListAsync();
        Assert.Contains("1234 AB-7", plates);
        Assert.Contains("5678 CD-4", plates);
        var homeVan = Assert.Single(db.Vehicles.Where(v => v.PlateNumber == "5678 CD-4"));
        Assert.Equal("Пуховичское райпо", homeVan.OperatorName);
        Assert.InRange(homeVan.LastLatitude ?? 0, 53.56, 53.59);
        Assert.InRange(homeVan.LastLongitude ?? 0, 27.73, 27.76);
    }
}
