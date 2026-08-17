using Avtomagazin.Contracts;
using Avtomagazin.Identity.Api.Data;

namespace Avtomagazin.Identity.Api;

internal static class IdentityMaps
{
    public static LoginResponse ToLogin(AppUser user, string? accessToken) => new(
        accessToken,
        user.Role,
        user.Id,
        user.Email,
        user.DisplayName,
        user.VehicleId,
        user.Status);

    public static StaffProfileDto ToProfile(AppUser user) => new(
        user.Id,
        user.Email,
        user.Role,
        user.DisplayName,
        user.DisplayName,
        user.LastName,
        user.FirstName,
        user.MiddleName,
        user.Phone,
        user.PhotoUrl,
        user.VehicleId,
        user.Status);

    public static AccountDto ToAccount(AppUser user) => new(
        user.Id,
        user.Email,
        user.DisplayName,
        user.Role,
        Roles.Title(user.Role),
        user.Status,
        user.VehicleId,
        user.CreatedAtUtc,
        user.ApprovedAtUtc);

    public static StaffMemberDto ToStaff(AppUser user) => new(
        user.Id,
        user.Email,
        user.DisplayName,
        user.Role,
        Roles.Title(user.Role),
        user.Status,
        user.VehicleId);

    public static UserStatusDto ToStatus(AppUser user) => new(user.Id, user.Status);
}
