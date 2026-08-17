using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Avtomagazin.Routing.Api.Data;

public sealed class RoutingDbContextFactory : IDesignTimeDbContextFactory<RoutingDbContext>
{
    public RoutingDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<RoutingDbContext>()
            .UseNpgsql("Host=localhost;Database=avtomagazin_routing;Username=avtomagazin;Password=avtomagazin")
            .Options;
        return new RoutingDbContext(options);
    }
}
