using Avtomagazin.Fleet.Api.Consumers;
using Avtomagazin.Fleet.Api.Data;
using Avtomagazin.Fleet.Api.Endpoints;
using Avtomagazin.Fleet.Api.Gps;
using Avtomagazin.ServiceDefaults;
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

app.MapVehicleEndpoints();
app.Run();

public partial class Program;
