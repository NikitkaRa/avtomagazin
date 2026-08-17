namespace Avtomagazin.Contracts;

public static class CaseStatuses
{
    public const string Open = "open";
    public const string InProgress = "in_progress";
    public const string Closed = "closed";

    public static bool IsKnown(string status) => status is Open or InProgress or Closed;

    public static string Title(string status) => status switch
    {
        Open => "открыта",
        InProgress => "в процессе",
        Closed => "закрыта",
        _ => status
    };
}

public static class CaseEventKinds
{
    public const string Report = "report";
    public const string Status = "status";
    public const string Comment = "comment";
}

public static class PresenceKinds
{
    public const string OnSite = "on-site";
    public const string NoShow = "no-show";

    public static bool IsKnown(string kind) => kind is OnSite or NoShow;
}
