using Avtomagazin.Contracts;
using Avtomagazin.Contracts.Events;
using Avtomagazin.Identity.Api.Data;
using MassTransit;
using Microsoft.EntityFrameworkCore;

namespace Avtomagazin.Identity.Api;

internal static partial class IdentityRoutes
{
    public const int MinPasswordLength = 8;
    private const int MaxFailedLogins = 8;
    private const int DefaultPageSize = 100;
    private const int MaxPageSize = 200;
    private static readonly TimeSpan LockoutFor = TimeSpan.FromMinutes(15);

    public static void Map(WebApplication app)
    {
        app.MapPost("/api/auth/register", RegisterAsync)
            .RequireRateLimiting("auth")
            .WithName("Register")
            .WithTags("Auth");
        app.MapPost("/api/auth/login", LoginAsync)
            .RequireRateLimiting("auth")
            .WithName("Login")
            .WithTags("Auth");
        app.MapPost("/api/auth/refresh", RefreshAsync)
            .RequireAuthorization()
            .WithName("Refresh")
            .WithTags("Auth");
        app.MapGet("/api/auth/me", MeAsync).RequireAuthorization().WithName("Me").WithTags("Auth");
        app.MapPatch("/api/auth/profile", UpdateProfileAsync)
            .RequireAuthorization()
            .WithName("UpdateProfile")
            .WithTags("Auth");
        app.MapGet("/api/internal/users/{id:guid}/session", SessionAsync)
            .WithName("InternalSession")
            .WithTags("Internal");

        app.MapGet("/api/users", ListUsersAsync)
            .RequireAuthorization(p => p.RequireRole(Roles.Admin))
            .WithName("ListUsers")
            .WithTags("Users");
        app.MapGet("/api/staff", ListStaffAsync)
            .RequireAuthorization(p => p.RequireRole(Roles.Admin, Roles.Operator))
            .WithName("ListStaff")
            .WithTags("Users");
        app.MapPost("/api/users/{id:guid}/assign-vehicle", AssignVehicleAsync)
            .RequireAuthorization(p => p.RequireRole(Roles.Admin, Roles.Operator))
            .WithName("AssignVehicle")
            .WithTags("Users");
        app.MapPost("/api/users/{id:guid}/approve", ApproveAsync)
            .RequireAuthorization(p => p.RequireRole(Roles.Admin))
            .WithName("ApproveUser")
            .WithTags("Users");
        app.MapPost("/api/users/{id:guid}/reject", RejectAsync)
            .RequireAuthorization(p => p.RequireRole(Roles.Admin))
            .WithName("RejectUser")
            .WithTags("Users");
        app.MapPost("/api/users/{id:guid}/disable", DisableAsync)
            .RequireAuthorization(p => p.RequireRole(Roles.Admin))
            .WithName("DisableUser")
            .WithTags("Users");
        app.MapPost("/api/users/{id:guid}/password", ResetPasswordAsync)
            .RequireAuthorization(p => p.RequireRole(Roles.Admin))
            .WithName("ResetPassword")
            .WithTags("Users");
        app.MapPost("/api/auth/password", ChangePasswordAsync)
            .RequireAuthorization()
            .WithName("ChangePassword")
            .WithTags("Auth");
    }

    private static string? TrimOrNull(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string? NormalizeEmail(string? email)
        => string.IsNullOrWhiteSpace(email) ? null : email.Trim().ToLowerInvariant();

    private static Task NotifyVehicleAsync(IPublishEndpoint bus, AppUser user)
        => bus.Publish(new StaffVehicleAssigned(
            user.Id,
            user.Role,
            user.VehicleId,
            user.DisplayName,
            user.Phone));

    private static async Task PublishVehicleChangesAsync(
        IPublishEndpoint bus,
        AppUser user,
        IReadOnlyList<AppUser> bumped)
    {
        await NotifyVehicleAsync(bus, user);
        foreach (var other in bumped)
        {
            await NotifyVehicleAsync(bus, other);
        }
    }

    private static async Task<List<AppUser>> VacateSameSeatAsync(
        IdentityDbContext db,
        Guid userId,
        string role,
        Guid? vehicleId)
    {
        var bumped = new List<AppUser>();
        if (vehicleId is not Guid assigned || !Roles.IsVanCrew(role))
        {
            return bumped;
        }

        var previous = await db.Users
            .Where(u => u.VehicleId == assigned && u.Id != userId && u.Role == role)
            .ToListAsync();
        foreach (var other in previous)
        {
            other.VehicleId = null;
            other.TokenVersion++;
            bumped.Add(other);
        }

        return bumped;
    }
}
