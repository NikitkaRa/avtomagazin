using Avtomagazin.Contracts;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Avtomagazin.Identity.Api.Data;

internal static class Seed
{
    public const string FixturePassword = "testpass1";

    private static readonly PasswordHasher<AppUser> Hasher = new();

    /// <summary>Local/Testing accounts so integration tests and empty DBs have roles to exercise.</summary>
    public static async Task EnsureFixtureUsersAsync(IdentityDbContext db)
    {
        var grodnoVan = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        var pukhovichiVan = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
        var rows =
            new (Guid Id, string Email, string Name, string Role, Guid? VehicleId)[]
            {
                (Guid.Parse("11111111-1111-1111-1111-111111111111"), "resident@test.local", "Житель", Roles.Resident, null),
                (Guid.Parse("22222222-2222-2222-2222-222222222222"), "driver@test.local", "Водитель Озеричино", Roles.Driver, pukhovichiVan),
                (Guid.Parse("55555555-5555-5555-5555-555555555555"), "seller@test.local", "Продавец Озеричино", Roles.Seller, pukhovichiVan),
                (Guid.Parse("66666666-6666-6666-6666-666666666666"), "driver2@test.local", "Водитель Гродно", Roles.Driver, grodnoVan),
                (Guid.Parse("33333333-3333-3333-3333-333333333333"), "operator@test.local", "Диспетчер", Roles.Operator, null),
                (Guid.Parse("44444444-4444-4444-4444-444444444444"), "admin@test.local", "Админ", Roles.Admin, null)
            };

        foreach (var row in rows)
        {
            if (await db.Users.AnyAsync(u => u.Email == row.Email))
            {
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
            user.PasswordHash = Hasher.HashPassword(user, FixturePassword);
            db.Users.Add(user);
        }

        await db.SaveChangesAsync();
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
