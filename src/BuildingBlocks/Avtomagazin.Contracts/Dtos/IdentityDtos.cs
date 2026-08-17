namespace Avtomagazin.Contracts;

public sealed record LoginRequest(string Email, string Password);

public sealed record RegisterRequest(
    string Email,
    string Password,
    string? Name,
    string? Client,
    string? StaffRole);

public sealed record LoginResponse(
    string? AccessToken,
    string Role,
    Guid UserId,
    string Email,
    string? Name,
    Guid? VehicleId,
    string? Status);

public sealed record StaffProfileDto(
    Guid Id,
    string Email,
    string Role,
    string? Name,
    string? DisplayName,
    string? LastName,
    string? FirstName,
    string? MiddleName,
    string? Phone,
    string? PhotoUrl,
    Guid? VehicleId,
    string? Status);

public sealed record UpdateProfileRequest(
    string? DisplayName = null,
    string? LastName = null,
    string? FirstName = null,
    string? MiddleName = null,
    string? Phone = null,
    string? PhotoDataUrl = null,
    bool? ClearPhoto = null);

public sealed record AccountDto(
    Guid Id,
    string Email,
    string DisplayName,
    string Role,
    string RoleTitle,
    string Status,
    Guid? VehicleId,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? ApprovedAtUtc);

public sealed record StaffMemberDto(
    Guid Id,
    string Email,
    string DisplayName,
    string Role,
    string RoleTitle,
    string Status,
    Guid? VehicleId);

public sealed record AssignVehicleRequest(Guid? VehicleId);

public sealed record ApproveUserRequest(string? Role, Guid? VehicleId);

public sealed record ChangePasswordRequest(string Current, string Next);

public sealed record SetPasswordRequest(string Password);

public sealed record UserStatusDto(Guid Id, string Status);
