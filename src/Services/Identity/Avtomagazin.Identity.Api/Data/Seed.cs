using Avtomagazin.Contracts;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Avtomagazin.Identity.Api.Data;

internal static class Seed
{
    private static readonly PasswordHasher<AppUser> Hasher = new();

    public static async Task EnsureDemoUsersAsync(IdentityDbContext db)
    {
        var grodnoVan = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        var pukhovichiVan = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
        var demo =
            new (Guid Id, string Email, string Name, string Role, Guid? VehicleId)[]
            {
                (Guid.Parse("11111111-1111-1111-1111-111111111111"), "resident@demo.by", "Житель", Roles.Resident, null),
                (Guid.Parse("22222222-2222-2222-2222-222222222222"), "driver@demo.by", "Водитель Озеричино", Roles.Driver, pukhovichiVan),
                (Guid.Parse("55555555-5555-5555-5555-555555555555"), "seller@demo.by", "Продавец Озеричино", Roles.Seller, pukhovichiVan),
                (Guid.Parse("66666666-6666-6666-6666-666666666666"), "driver2@demo.by", "Водитель Гродно", Roles.Driver, grodnoVan),
                (Guid.Parse("33333333-3333-3333-3333-333333333333"), "operator@demo.by", "Диспетчер", Roles.Operator, null),
                (Guid.Parse("44444444-4444-4444-4444-444444444444"), "admin@demo.by", "Админ", Roles.Admin, null)
            };

        foreach (var row in demo)
        {
            var existing = await db.Users.FirstOrDefaultAsync(u => u.Email == row.Email);
            if (existing is not null)
            {
                if (existing.VehicleId != row.VehicleId)
                {
                    existing.VehicleId = row.VehicleId;
                }

                existing.DisplayName = row.Name;
                existing.Role = row.Role;
                existing.Status = UserStatuses.Active;
                existing.ApprovedAtUtc ??= DateTimeOffset.UtcNow;
                continue;
            }

            var user = new AppUser
            {
                Id = row.Id,
                Email = row.Email,
                DisplayName = row.Name,
                Role = row.Role,
                Status = UserStatuses.Active,
                PasswordHash = "",
                TokenVersion = 1,
                VehicleId = row.VehicleId,
                CreatedAtUtc = DateTimeOffset.UtcNow,
                ApprovedAtUtc = DateTimeOffset.UtcNow
            };
            user.PasswordHash = Hasher.HashPassword(user, "demo");
            db.Users.Add(user);
        }

        await db.SaveChangesAsync();
    }

    public static async Task EnsureHeatResidentsAsync(IdentityDbContext db)
    {
        var marker = DemoHeatCatalog.ResidentId(0);
        if (await db.Users.AnyAsync(u => u.Id == marker))
        {
            return;
        }

        var now = DateTimeOffset.UtcNow;
        var proto = new AppUser
        {
            Id = marker,
            Email = "heat0@demo.by",
            DisplayName = "Житель",
            Role = Roles.Resident,
            Status = UserStatuses.Active,
            PasswordHash = ""
        };
        var hash = Hasher.HashPassword(proto, "demo");
        const int batch = 200;
        for (var start = 0; start < DemoHeatCatalog.ResidentCount; start += batch)
        {
            var end = Math.Min(start + batch, DemoHeatCatalog.ResidentCount);
            for (var i = start; i < end; i++)
            {
                db.Users.Add(new AppUser
                {
                    Id = DemoHeatCatalog.ResidentId(i),
                    Email = $"heat{i}@demo.by",
                    DisplayName = $"Житель {i + 1}",
                    Role = Roles.Resident,
                    Status = UserStatuses.Active,
                    PasswordHash = hash,
                    TokenVersion = 1,
                    CreatedAtUtc = now,
                    ApprovedAtUtc = now
                });
            }

            await db.SaveChangesAsync();
        }
    }

    public static async Task EnsureBootstrapAdminAsync(IdentityDbContext db, IConfiguration config)
    {
        var email = config["Bootstrap:AdminEmail"]?.Trim().ToLowerInvariant();
        var password = config["Bootstrap:AdminPassword"];
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
        {
            return;
        }

        if (await db.Users.AnyAsync(u => u.Role == Roles.Admin))
        {
            return;
        }

        var user = new AppUser
        {
            Id = Guid.NewGuid(),
            Email = email,
            DisplayName = "Админ",
            Role = Roles.Admin,
            Status = UserStatuses.Active,
            PasswordHash = "",
            TokenVersion = 1,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            ApprovedAtUtc = DateTimeOffset.UtcNow
        };
        user.PasswordHash = Hasher.HashPassword(user, password);
        db.Users.Add(user);
        await db.SaveChangesAsync();
    }
}
