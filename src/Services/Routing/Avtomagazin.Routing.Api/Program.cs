using Avtomagazin.Routing.Api.Data;
using Avtomagazin.Routing.Api.Endpoints;
using Avtomagazin.Routing.Api.Gov;
using Avtomagazin.ServiceDefaults;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);
builder.AddAvtomagazinDefaults();
builder.AddAvtomagazinObjectStorage();

var routingCs = DeploySecrets.ConnectionString(
    builder.Configuration,
    builder.Environment,
    "Routing",
    "Host=localhost;Port=5432;Database=avtomagazin_routing;Username=avtomagazin;Password=avtomagazin");

var routingTestDb = $"avtomagazin-routing-tests-{Guid.NewGuid()}";
builder.Services.AddDbContext<RoutingDbContext>(options =>
{
    if (builder.Environment.IsEnvironment("Testing"))
    {
        options.UseInMemoryDatabase(routingTestDb);
        return;
    }

    options.UseNpgsql(routingCs);
});

if (!builder.Environment.IsEnvironment("Testing"))
{
    builder.AddAvtomagazinPostgresHealth("postgres", routingCs);
}

builder.Services.AddSingleton<IGovIntegration, StubGovIntegration>();

var app = builder.Build();
app.UseAvtomagazinDefaults();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<RoutingDbContext>();
    await RelationalSchema.ApplyAsync(db);
    var seedSample = app.Configuration.GetValue(
        "Seed:SampleData",
        app.Environment.IsDevelopment() || app.Environment.IsEnvironment("Testing"));
    if (seedSample)
    {
        await Seed.EnsureSeedAsync(db);
    }
}

app.MapRoutingApi();

app.Run();

public partial class Program;
