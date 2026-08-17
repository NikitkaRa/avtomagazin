using Avtomagazin.Fleet.Api.Data;

namespace Avtomagazin.Fleet.Api;

internal static class VehicleCrew
{
    public static void ApplySnapshot(Vehicle vehicle, UpsertVehicleRequest request)
    {
        vehicle.DriverUserId = request.DriverUserId;
        if (request.DriverUserId is null)
        {
            vehicle.DriverName = null;
            vehicle.DriverPhone = null;
        }
        else
        {
            vehicle.DriverName = VehiclePhotos.TrimOrNull(request.DriverName);
            vehicle.DriverPhone = VehiclePhotos.TrimOrNull(request.DriverPhone);
        }

        vehicle.SellerUserId = request.SellerUserId;
        if (request.SellerUserId is null)
        {
            vehicle.SellerName = null;
            vehicle.SellerPhone = null;
        }
        else
        {
            vehicle.SellerName = VehiclePhotos.TrimOrNull(request.SellerName);
            vehicle.SellerPhone = VehiclePhotos.TrimOrNull(request.SellerPhone);
        }
    }
}
