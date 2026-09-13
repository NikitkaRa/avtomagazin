namespace Avtomagazin.Admin;

public sealed class RouteDraftStop
{
    public Guid? Id { get; set; }
    public string Name { get; set; } = "";
    public double Lat { get; set; }
    public double Lng { get; set; }
    public DateTimeOffset? PlannedArrivalUtc { get; set; }
    public string? PhotoDataUrl { get; set; }
    public bool ClearPhoto { get; set; }
    public bool PhotoDirty { get; set; }
}
