using System.Globalization;
using Emplyo.Domain.Enums;

namespace Emplyo.Web.Services;

public static class Ui
{
    public static string Humanize(Enum value)
    {
        var text = value.ToString();
        return string.Concat(text.Select((c, i) => i > 0 && char.IsUpper(c) ? " " + c : c.ToString()));
    }

    public static string EmploymentStatusBadge(EmploymentStatus status)
        => Badge(status switch
        {
            Domain.Enums.EmploymentStatus.Pending => "pending",
            Domain.Enums.EmploymentStatus.Active => "active",
            Domain.Enums.EmploymentStatus.OnLeave => "onleave",
            Domain.Enums.EmploymentStatus.Suspended => "suspended",
            Domain.Enums.EmploymentStatus.Terminated => "terminated",
            _ => "draft"
        }, Humanize(status));

    public static string EmploymentTypeLabel(EmploymentType type) => type switch
    {
        Domain.Enums.EmploymentType.FullTime => "Full time",
        Domain.Enums.EmploymentType.PartTime => "Part time",
        Domain.Enums.EmploymentType.Contract => "Contract",
        Domain.Enums.EmploymentType.Intern => "Intern",
        _ => Humanize(type)
    };

    public static string LeaveStatusBadge(LeaveRequestStatus status)
        => Badge(status switch
        {
            Domain.Enums.LeaveRequestStatus.Pending => "pending",
            Domain.Enums.LeaveRequestStatus.Approved => "approved",
            Domain.Enums.LeaveRequestStatus.Rejected => "rejected",
            Domain.Enums.LeaveRequestStatus.Cancelled => "cancelled",
            _ => "draft"
        }, Humanize(status));

    public static string AttendanceStatusBadge(AttendanceStatus status)
        => Badge(status switch
        {
            Domain.Enums.AttendanceStatus.Present => "present",
            Domain.Enums.AttendanceStatus.Late => "late",
            Domain.Enums.AttendanceStatus.Absent => "absent",
            Domain.Enums.AttendanceStatus.HalfDay => "halfday",
            Domain.Enums.AttendanceStatus.Remote => "remote",
            Domain.Enums.AttendanceStatus.Holiday => "draft",
            Domain.Enums.AttendanceStatus.OnLeave => "onleave",
            _ => "draft"
        }, Humanize(status));

    public static string TaskStatusBadge(TaskItemStatus status)
        => Badge(status switch
        {
            Domain.Enums.TaskItemStatus.Pending => "pending",
            Domain.Enums.TaskItemStatus.InProgress => "inprogress",
            Domain.Enums.TaskItemStatus.Completed => "completed",
            Domain.Enums.TaskItemStatus.Cancelled => "cancelled",
            _ => "draft"
        }, Humanize(status));

    public static string OffboardingStatusBadge(OffboardingStatus status)
        => Badge(status switch
        {
            Domain.Enums.OffboardingStatus.Initiated => "initiated",
            Domain.Enums.OffboardingStatus.InProgress => "inprogress",
            Domain.Enums.OffboardingStatus.Completed => "completed",
            Domain.Enums.OffboardingStatus.Cancelled => "cancelled",
            _ => "draft"
        }, Humanize(status));

    public static string ActiveBadge(bool active)
        => Badge(active ? "active" : "inactive", active ? "Active" : "Inactive");

    public static string PublishedBadge(bool published)
        => Badge(published ? "published" : "draft", published ? "Published" : "Draft");

    public static string OutcomeBadge(AuditOutcome outcome)
        => Badge(outcome == AuditOutcome.Success ? "approved" : "rejected", Humanize(outcome));

    private static string Badge(string state, string text)
        => $"<span class=\"badge-status is-{state}\">{System.Net.WebUtility.HtmlEncode(text)}</span>";

    public static string Initials(string? fullName)
    {
        if (string.IsNullOrWhiteSpace(fullName)) return "?";
        var parts = fullName.Split(' ', StringSplitOptions.RemoveEmptyEntries).Take(2);
        return string.Concat(parts.Select(p => char.ToUpperInvariant(p[0])));
    }

    public static string FileSize(long bytes)
    {
        string[] units = { "B", "KB", "MB", "GB" };
        double size = bytes;
        var unit = 0;
        while (size >= 1024 && unit < units.Length - 1) { size /= 1024; unit++; }
        return string.Format(CultureInfo.InvariantCulture, "{0:0.#} {1}", size, units[unit]);
    }
}
