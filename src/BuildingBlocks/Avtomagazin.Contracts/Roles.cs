namespace Avtomagazin.Contracts;

public static class Roles
{
    /// <summary>Житель деревни. Регается в приложении жителя, сразу активен.</summary>
    public const string Resident = "resident";

    /// <summary>Водитель автолавки — ведёт машину, часто шлёт GPS.</summary>
    public const string Driver = "driver";

    /// <summary>Продавец в автолавке — торгует в салоне.</summary>
    public const string Seller = "seller";

    /// <summary>Диспетчер: в офисе, жалобы и операционка.</summary>
    public const string Operator = "operator";

    /// <summary>Админ райпо: подтверждает персонал. Обычно один.</summary>
    public const string Admin = "admin";

    public static string Title(string role) => role switch
    {
        Resident => "Житель",
        Driver => "Водитель",
        Seller => "Продавец",
        Operator => "Диспетчер",
        Admin => "Админ",
        _ => role
    };

    public static bool IsStaff(string role) => role is Driver or Seller or Operator or Admin;

    public static bool IsVanCrew(string role) => role is Driver or Seller;
}

public static class UserStatuses
{
    public const string Pending = "pending";
    public const string Active = "active";
    public const string Disabled = "disabled";
}

public static class AuthClients
{
    public const string Resident = "resident";
    public const string Staff = "staff";
}
