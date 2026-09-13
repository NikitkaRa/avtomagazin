using Avtomagazin.Contracts;
using Avtomagazin.Identity.Api.Data;
using MassTransit;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Avtomagazin.Identity.Api;

internal static partial class IdentityRoutes
{
    private static async Task<IResult> ListUsersAsync(
        string? status,
        string? role,
        string? kind,
        int? skip,
        int? take,
        IdentityDbContext db)
    {
        var query = db.Users.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(status))
        {
            query = query.Where(u => u.Status == status);
        }

        var audience = (kind ?? "").Trim().ToLowerInvariant();
        if (audience is "staff")
        {
            query = query.Where(u => u.Role != Roles.Resident);
        }
        else if (audience is "resident" or "residents")
        {
            query = query.Where(u => u.Role == Roles.Resident);
        }

        if (!string.IsNullOrWhiteSpace(role))
        {
            query = query.Where(u => u.Role == role);
        }

        var pageSkip = Math.Max(skip ?? 0, 0);
        var pageTake = Math.Clamp(take ?? DefaultPageSize, 1, MaxPageSize);
        var total = await query.CountAsync();
        var items = await query
            .OrderBy(u => u.Status == UserStatuses.Pending ? 0 : 1)
            .ThenBy(u => u.DisplayName)
            .Skip(pageSkip)
            .Take(pageTake)
            .ToListAsync();

        return Results.Ok(new PagedResult<AccountDto>(
            items.Select(IdentityMaps.ToAccount).ToList(),
            total,
            pageSkip,
            pageTake));
    }

    private static async Task<IResult> ListStaffAsync(IdentityDbContext db)
    {
        var items = await db.Users.AsNoTracking()
            .Where(u => u.Status == UserStatuses.Active
                        && (u.Role == Roles.Driver
                            || u.Role == Roles.Seller
                            || u.Role == Roles.Operator
                            || u.Role == Roles.Admin))
            .OrderBy(u => u.DisplayName)
            .ToListAsync();

        return Results.Ok(items.Select(IdentityMaps.ToStaff));
    }

    private static async Task<IResult> AssignVehicleAsync(
        Guid id,
        [FromBody] AssignVehicleRequest? request,
        IdentityDbContext db,
        IPublishEndpoint bus)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == id);
        if (user is null)
        {
            return Results.NotFound();
        }

        if (user.Status != UserStatuses.Active)
        {
            return Results.BadRequest(new { error = "Можно назначать только активных сотрудников" });
        }

        if (user.Role is not (Roles.Driver or Roles.Seller))
        {
            return Results.BadRequest(new { error = "На автолавку назначают водителя или продавца" });
        }

        var vehicleId = request?.VehicleId is Guid van && van != Guid.Empty ? van : (Guid?)null;
        var bumped = await VacateSameSeatAsync(db, id, user.Role, vehicleId);
        user.VehicleId = vehicleId;
        user.TokenVersion++;
        await db.SaveChangesAsync();
        await PublishVehicleChangesAsync(bus, user, bumped);

        return Results.Ok(IdentityMaps.ToStaff(user));
    }

    private static async Task<IResult> ApproveAsync(
        Guid id,
        [FromBody] ApproveUserRequest? request,
        IdentityDbContext db,
        IPublishEndpoint bus)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == id);
        if (user is null)
        {
            return Results.NotFound();
        }

        if (user.Role == Roles.Admin)
        {
            return Results.BadRequest(new { error = "Админа так не трогаем" });
        }

        var nextRole = (request?.Role ?? user.Role).Trim().ToLowerInvariant();
        if (nextRole is not (Roles.Driver or Roles.Seller or Roles.Operator or Roles.Resident))
        {
            return Results.BadRequest(new { error = "Роль: resident, driver, seller или operator" });
        }

        if (request?.VehicleId is Guid van)
        {
            user.VehicleId = van == Guid.Empty ? null : van;
        }

        if (nextRole == Roles.Driver && user.VehicleId is null)
        {
            return Results.BadRequest(new { error = "Водителю нужна автолавка" });
        }

        var wasDisabled = user.Status == UserStatuses.Disabled;
        user.Role = nextRole;
        user.Status = UserStatuses.Active;
        user.ApprovedAtUtc = DateTimeOffset.UtcNow;
        if (wasDisabled)
        {
            user.TokenVersion++;
        }

        var bumped = await VacateSameSeatAsync(db, id, user.Role, user.VehicleId);
        await db.SaveChangesAsync();
        if (Roles.IsVanCrew(user.Role))
        {
            await PublishVehicleChangesAsync(bus, user, bumped);
        }

        return Results.Ok(IdentityMaps.ToAccount(user));
    }

    private static async Task<IResult> RejectAsync(Guid id, IdentityDbContext db)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == id);
        if (user is null)
        {
            return Results.NotFound();
        }

        if (user.Status != UserStatuses.Pending)
        {
            return Results.BadRequest(new { error = "Отклонить можно только заявку" });
        }

        db.Users.Remove(user);
        await db.SaveChangesAsync();
        return Results.NoContent();
    }

    private static async Task<IResult> DisableAsync(Guid id, IdentityDbContext db, IPublishEndpoint bus)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == id);
        if (user is null)
        {
            return Results.NotFound();
        }

        if (user.Role == Roles.Admin)
        {
            return Results.BadRequest(new { error = "Админа так не трогаем" });
        }

        user.Status = UserStatuses.Disabled;
        user.TokenVersion++;
        user.VehicleId = null;
        await db.SaveChangesAsync();
        if (Roles.IsVanCrew(user.Role))
        {
            await NotifyVehicleAsync(bus, user);
        }

        return Results.Ok(IdentityMaps.ToStatus(user));
    }

    private static async Task<IResult> ResetPasswordAsync(
        Guid id,
        [FromBody] SetPasswordRequest request,
        IdentityDbContext db,
        PasswordHasher<AppUser> hasher)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == id);
        if (user is null)
        {
            return Results.NotFound();
        }

        if (user.Role == Roles.Admin)
        {
            return Results.BadRequest(new { error = "Админа так не трогаем" });
        }

        if (string.IsNullOrWhiteSpace(request.Password) || request.Password.Length < MinPasswordLength)
        {
            return Results.BadRequest(new { error = $"Пароль от {MinPasswordLength} символов" });
        }

        user.PasswordHash = hasher.HashPassword(user, request.Password);
        user.TokenVersion++;
        user.FailedLoginCount = 0;
        user.LockoutEndUtc = null;
        await db.SaveChangesAsync();
        return Results.Ok(IdentityMaps.ToStatus(user));
    }
}
