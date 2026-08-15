using System.Security.Claims;
using Avtomagazin.Contracts;
using Microsoft.AspNetCore.Http;

namespace Avtomagazin.ServiceDefaults;

public static class HttpAccess
{
    public static IResult? ForbidVehicleWrite(ClaimsPrincipal user, Guid vehicleId)
    {
        if (user.CanWriteVehicle(vehicleId))
        {
            return null;
        }

        if (user.Role() == Roles.Driver && user.AssignedVehicleId() is null)
        {
            return Results.Json(new { error = "Админ ещё не назначил автолавку" }, statusCode: StatusCodes.Status403Forbidden);
        }

        return Results.Json(new { error = "Это не ваша автолавка" }, statusCode: StatusCodes.Status403Forbidden);
    }
}
