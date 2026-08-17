using Avtomagazin.Contracts;
using Avtomagazin.Fleet.Api.Data;

namespace Avtomagazin.Fleet.Api;

internal static class VehicleCrew
{
    public static void ClearUser(Vehicle vehicle, Guid userId)
    {
        if (vehicle.DriverUserId == userId)
        {
            vehicle.DriverUserId = null;
            vehicle.DriverName = null;
            vehicle.DriverPhone = null;
        }

        if (vehicle.SellerUserId == userId)
        {
            vehicle.SellerUserId = null;
            vehicle.SellerName = null;
            vehicle.SellerPhone = null;
        }
    }

    public static void Assign(Vehicle vehicle, string role, Guid userId, string? name, string? phone)
    {
        var trimmedName = string.IsNullOrWhiteSpace(name) ? null : name.Trim();
        var trimmedPhone = string.IsNullOrWhiteSpace(phone) ? null : phone.Trim();
        if (role == Roles.Driver)
        {
            vehicle.DriverUserId = userId;
            vehicle.DriverName = trimmedName;
            vehicle.DriverPhone = trimmedPhone;
            return;
        }

        if (role == Roles.Seller)
        {
            vehicle.SellerUserId = userId;
            vehicle.SellerName = trimmedName;
            vehicle.SellerPhone = trimmedPhone;
        }
    }
}
