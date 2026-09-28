namespace Emplyo.Web.Authorization;

public static class Roles
{
    public const string Administrator = "Administrator";
    public const string HrManager = "HRManager";
    public const string Employee = "Employee";
}

public static class Policies
{
    public const string Administrator = "RequireAdministrator";
    public const string HrManager = "RequireHrManager";
    public const string HrOrAdmin = "RequireHrOrAdmin";
    public const string Authenticated = "RequireAuthenticated";
}
