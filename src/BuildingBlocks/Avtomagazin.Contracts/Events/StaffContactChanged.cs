namespace Avtomagazin.Contracts.Events;

/// <summary>
/// Identity owns person truth; Fleet keeps a denormalized snapshot on vehicles for display.
/// </summary>
public sealed record StaffContactChanged(
    Guid UserId,
    string DisplayName,
    string? Phone);
