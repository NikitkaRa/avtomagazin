using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Avtomagazin.Fleet.Api.Data;

public sealed class FleetDbContextFactory : IDesignTimeDbContextFactory<FleetDbContext>
{
    public FleetDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<FleetDbContext>()
            .UseNpgsql("Host=localhost;Database=avtomagazin_fleet;Username=avtomagazin;Password=avtomagazin")
            .Options;
        return new FleetDbContext(options);
    }
}
