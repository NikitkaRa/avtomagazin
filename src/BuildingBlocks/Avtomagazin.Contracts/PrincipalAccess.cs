using System.Security.Claims;

namespace Avtomagazin.Contracts;

public static class AuthClaims
{
    public const string VehicleId = "vehicleId";
    public const string TokenVersion = "tv";
}

public static class PrincipalAccess
{
    public static string? Role(this ClaimsPrincipal user)
        => user.FindFirst(ClaimTypes.Role)?.Value;

    public static Guid? UserId(this ClaimsPrincipal user)
    {
        var raw = user.FindFirst("sub")?.Value
                  ?? user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return Guid.TryParse(raw, out var id) ? id : null;
    }

    public static Guid? AssignedVehicleId(this ClaimsPrincipal user)
        => Guid.TryParse(user.FindFirst(AuthClaims.VehicleId)?.Value, out var id) ? id : null;

    public static int TokenVersion(this ClaimsPrincipal user)
        => int.TryParse(user.FindFirst(AuthClaims.TokenVersion)?.Value, out var v) ? v : 0;

    public static bool CanDispatch(this ClaimsPrincipal user)
        => user.Role() is Roles.Operator or Roles.Admin;

    public static bool CanWriteVehicle(this ClaimsPrincipal user, Guid vehicleId)
    {
        if (user.CanDispatch())
        {
            return true;
        }

        return user.Role() == Roles.Driver && user.AssignedVehicleId() == vehicleId;
    }
}
