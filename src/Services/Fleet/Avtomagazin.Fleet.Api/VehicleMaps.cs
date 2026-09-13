using Avtomagazin.Contracts;
using Avtomagazin.Fleet.Api.Data;

namespace Avtomagazin.Fleet.Api;

internal static class VehicleMaps
{
    public static VehicleDto ToDto(this Vehicle vehicle, bool includeContacts = true) => new(
        vehicle.Id,
        vehicle.PlateNumber,
        vehicle.OperatorName,
        vehicle.IsActive,
        vehicle.LastLatitude,
        vehicle.LastLongitude,
        vehicle.LastSeenAtUtc,
        vehicle.LastSource,
        includeContacts ? vehicle.DriverName : null,
        includeContacts ? vehicle.DriverPhone : null,
        includeContacts ? vehicle.SellerName : null,
        includeContacts ? vehicle.SellerPhone : null,
        includeContacts ? vehicle.OperatorPhone : null,
        includeContacts ? vehicle.DriverUserId : null,
        includeContacts ? vehicle.SellerUserId : null,
        vehicle.PhotoDataUrl);

    public static VehiclePositionDto ToDto(this VehiclePosition position) => new(
        position.Id,
        position.VehicleId,
        position.Latitude,
        position.Longitude,
        position.SpeedKmh,
        position.RecordedAtUtc,
        position.Source);
}
