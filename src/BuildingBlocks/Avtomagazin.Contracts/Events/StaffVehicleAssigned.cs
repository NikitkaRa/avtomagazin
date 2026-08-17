namespace Avtomagazin.Contracts.Events;

/// <summary>
/// Identity owns who sits in which van. Fleet applies a denormalized crew snapshot.
/// VehicleId null means the person was taken off the van.
/// </summary>
public sealed record StaffVehicleAssigned(
    Guid UserId,
    string Role,
    Guid? VehicleId,
    string? DisplayName,
    string? Phone);
