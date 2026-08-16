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
    builder.Services.AddDbContext<IdentityDbContext>(options =>
        options.UseInMemoryDatabase("avtomagazin-identity-tests"));
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
    await db.Database.EnsureCreatedAsync();
    if (db.Database.IsRelational())
    {
        await db.Database.ExecuteSqlRawAsync(
            """ALTER TABLE "Users" ADD COLUMN IF NOT EXISTS "Status" character varying(32) NOT NULL DEFAULT 'active';""");
        await db.Database.ExecuteSqlRawAsync(
            """ALTER TABLE "Users" ADD COLUMN IF NOT EXISTS "ApprovedAtUtc" timestamp with time zone;""");
        await db.Database.ExecuteSqlRawAsync(
            """ALTER TABLE "Users" ADD COLUMN IF NOT EXISTS "TokenVersion" integer NOT NULL DEFAULT 1;""");
        await db.Database.ExecuteSqlRawAsync(
            """ALTER TABLE "Users" ADD COLUMN IF NOT EXISTS "FailedLoginCount" integer NOT NULL DEFAULT 0;""");
        await db.Database.ExecuteSqlRawAsync(
            """ALTER TABLE "Users" ADD COLUMN IF NOT EXISTS "LockoutEndUtc" timestamp with time zone;""");
        await db.Database.ExecuteSqlRawAsync(
            """ALTER TABLE "Users" ADD COLUMN IF NOT EXISTS "LastName" character varying(80);""");
        await db.Database.ExecuteSqlRawAsync(
            """ALTER TABLE "Users" ADD COLUMN IF NOT EXISTS "FirstName" character varying(80);""");
        await db.Database.ExecuteSqlRawAsync(
            """ALTER TABLE "Users" ADD COLUMN IF NOT EXISTS "MiddleName" character varying(80);""");
        await db.Database.ExecuteSqlRawAsync(
            """ALTER TABLE "Users" ADD COLUMN IF NOT EXISTS "Phone" character varying(32);""");
        await db.Database.ExecuteSqlRawAsync(
            """ALTER TABLE "Users" ADD COLUMN IF NOT EXISTS "PhotoUrl" character varying(1000);""");
        await db.Database.ExecuteSqlRawAsync(
            """UPDATE "Users" SET "Status" = 'active' WHERE "Status" IS NULL OR "Status" = '';""");
        await db.Database.ExecuteSqlRawAsync(
            """UPDATE "Users" SET "TokenVersion" = 1 WHERE "TokenVersion" < 1;""");
    }

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
