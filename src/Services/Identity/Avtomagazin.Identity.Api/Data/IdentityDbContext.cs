using Microsoft.EntityFrameworkCore;

namespace Avtomagazin.Identity.Api.Data;

public sealed class IdentityDbContext(DbContextOptions<IdentityDbContext> options) : DbContext(options)
{
    public DbSet<AppUser> Users => Set<AppUser>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<AppUser>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.Email).IsUnique();
            e.Property(x => x.Email).HasMaxLength(320);
            e.Property(x => x.DisplayName).HasMaxLength(200);
            e.Property(x => x.LastName).HasMaxLength(80);
            e.Property(x => x.FirstName).HasMaxLength(80);
            e.Property(x => x.MiddleName).HasMaxLength(80);
            e.Property(x => x.Phone).HasMaxLength(32);
            e.Property(x => x.PhotoUrl).HasMaxLength(1000);
            e.Property(x => x.Role).HasMaxLength(32);
            e.Property(x => x.Status).HasMaxLength(32);
            e.Property(x => x.PasswordHash).HasMaxLength(500);
            e.Property(x => x.TokenVersion).HasDefaultValue(1);
        });
    }
}

public sealed class AppUser
{
    public Guid Id { get; set; }
    public required string Email { get; set; }
    public required string DisplayName { get; set; }
    public string? LastName { get; set; }
    public string? FirstName { get; set; }
    public string? MiddleName { get; set; }
    public string? Phone { get; set; }
    public string? PhotoUrl { get; set; }
    public required string Role { get; set; }
    public required string Status { get; set; }
    public required string PasswordHash { get; set; }
    public Guid? VehicleId { get; set; }
    public int TokenVersion { get; set; } = 1;
    public int FailedLoginCount { get; set; }
    public DateTimeOffset? LockoutEndUtc { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset? ApprovedAtUtc { get; set; }

    public void SyncDisplayName()
    {
        var parts = new[] { LastName, FirstName, MiddleName }
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x!.Trim())
            .ToArray();
        if (parts.Length > 0)
        {
            DisplayName = string.Join(' ', parts);
        }
    }
}
