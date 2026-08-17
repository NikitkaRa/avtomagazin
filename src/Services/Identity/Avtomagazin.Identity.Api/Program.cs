using Avtomagazin.Identity.Api;
using Avtomagazin.Identity.Api.Data;
using Avtomagazin.ServiceDefaults;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);
builder.AddAvtomagazinDefaults();
builder.AddAvtomagazinObjectStorage();
DeploySecrets.EnsureInternalKey(builder.Configuration, builder.Environment);

builder.Services.AddScoped<ISessionGuard, IdentityDbSessionGuard>();
builder.Services.AddSingleton<PasswordHasher<AppUser>>();

var identityCs = DeploySecrets.ConnectionString(
    builder.Configuration,
    builder.Environment,
    "Identity",
    "Host=localhost;Port=5432;Database=avtomagazin_identity;Username=avtomagazin;Password=avtomagazin");

if (builder.Environment.IsEnvironment("Testing"))
{
    var identityTestDb = $"avtomagazin-identity-tests-{Guid.NewGuid()}";
    builder.Services.AddDbContext<IdentityDbContext>(options =>
        options.UseInMemoryDatabase(identityTestDb));
}
else
{
    builder.Services.AddDbContext<IdentityDbContext>(options => options.UseNpgsql(identityCs));
    builder.AddAvtomagazinPostgresHealth("postgres", identityCs);
}

var app = builder.Build();
app.UseAvtomagazinDefaults();

if (!app.Environment.IsEnvironment("Testing"))
{
    await Postgres.EnsureDatabaseAsync(identityCs);
}

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
    await RelationalSchema.ApplyAsync(db);

    var seedDemo = builder.Configuration.GetValue("Seed:DemoUsers", builder.Environment.IsDevelopment() || builder.Environment.IsEnvironment("Testing"));
    if (seedDemo)
    {
        await Seed.EnsureDemoUsersAsync(db);
        if (builder.Environment.IsDevelopment())
        {
            await Seed.EnsureHeatResidentsAsync(db);
        }
    }

    await Seed.EnsureBootstrapAdminAsync(db, builder.Configuration);
}

IdentityRoutes.Map(app);
app.Run();

public partial class Program;
