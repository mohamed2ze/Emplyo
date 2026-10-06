using Emplyo.Application.LeaveManagement;
using Emplyo.Application.Organization;
using Emplyo.Application.Reporting;
using Emplyo.Domain.Enums;
using Emplyo.Web.Authorization;
using Emplyo.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Emplyo.Web.Controllers;

[Authorize(Policy = Policies.HrOrAdmin)]
public class ReportsController : Controller
{
    private readonly IReportService _reports;
    private readonly IDepartmentService _departments;
    private readonly ILeaveTypeService _leaveTypes;

    public ReportsController(IReportService reports, IDepartmentService departments, ILeaveTypeService leaveTypes)
    {
        _reports = reports;
        _departments = departments;
        _leaveTypes = leaveTypes;
    }

    public async Task<IActionResult> Index(int? year, int? departmentId, CancellationToken ct)
    {
        ViewData["Title"] = "Reports";
        ViewBag.Year = year ?? DateTime.UtcNow.Year;
        ViewBag.DepartmentId = departmentId;
        ViewBag.Departments = await _departments.GetLookupAsync(false, ct);

        ViewBag.Headcount = await _reports.GetHeadcountByDepartmentAsync(ct);
        ViewBag.StatusSummary = await _reports.GetStatusSummaryAsync(ct);
        ViewBag.RecentHires = await _reports.GetRecentHiresAsync(10, ct);
        ViewBag.LeaveUtilization = await _reports.GetLeaveUtilizationAsync(year ?? DateTime.UtcNow.Year, departmentId, ct);
        return View();
    }

    public async Task<IActionResult> Attendance(DateOnly? from, DateOnly? to, int? departmentId, CancellationToken ct)
    {
        var end = to ?? DateOnly.FromDateTime(DateTime.UtcNow);
        var start = from ?? end.AddDays(-30);
        ViewData["Title"] = "Attendance report";
        ViewBag.From = start;
        ViewBag.To = end;
        ViewBag.DepartmentId = departmentId;
        ViewBag.Departments = await _departments.GetLookupAsync(false, ct);
        return View(await _reports.GetAttendanceReportAsync(start, end, departmentId, ct));
    }

    [HttpGet]
    public async Task<IActionResult> ExportHeadcount(CancellationToken ct)
    {
        var data = await _reports.GetHeadcountByDepartmentAsync(ct);
        var lines = new List<string> { Csv.Line("Department", "Active", "Total") };
        lines.AddRange(data.Select(d => Csv.Line(d.DepartmentName, d.ActiveCount.ToString(), d.TotalCount.ToString())));
        return CsvFile(lines, "headcount-by-department");
    }

    [HttpGet]
    public async Task<IActionResult> ExportEmployees(int? departmentId, EmploymentStatus? status, CancellationToken ct)
    {
        var data = await _reports.GetEmployeeDirectoryAsync(departmentId, status, ct);
        var lines = new List<string> { Csv.Line("EmployeeNumber", "Name", "WorkEmail", "Department", "Position", "Manager", "Type", "Status", "HireDate", "Location") };
        lines.AddRange(data.Select(e => Csv.Line(e.EmployeeNumber, e.FullName, e.WorkEmail, e.DepartmentName, e.PositionTitle,
            e.ManagerName, Ui.EmploymentTypeLabel(e.EmploymentType), e.Status.ToString(), e.HireDate.ToString("yyyy-MM-dd"), e.WorkLocation)));
        return CsvFile(lines, "employee-directory");
    }

    [HttpGet]
    public async Task<IActionResult> ExportLeaveUtilization(int? year, int? departmentId, CancellationToken ct)
    {
        var y = year ?? DateTime.UtcNow.Year;
        var data = await _reports.GetLeaveUtilizationAsync(y, departmentId, ct);
        var lines = new List<string> { Csv.Line("EmployeeNumber", "Employee", "Department", "LeaveType", "Year", "Entitlement", "Used", "Remaining") };
        lines.AddRange(data.Select(l => Csv.Line(l.EmployeeNumber, l.EmployeeName, l.DepartmentName, l.LeaveTypeName,
            l.Year.ToString(), l.Entitlement.ToString("0.##"), l.Used.ToString("0.##"), l.Remaining.ToString("0.##"))));
        return CsvFile(lines, $"leave-utilization-{y}");
    }

    [HttpGet]
    public async Task<IActionResult> ExportAttendance(DateOnly? from, DateOnly? to, int? departmentId, CancellationToken ct)
    {
        var end = to ?? DateOnly.FromDateTime(DateTime.UtcNow);
        var start = from ?? end.AddDays(-30);
        var data = await _reports.GetAttendanceReportAsync(start, end, departmentId, ct);
        var lines = new List<string> { Csv.Line("EmployeeNumber", "Employee", "Department", "Present", "Late", "Absent", "Remote", "TotalHours") };
        lines.AddRange(data.Select(a => Csv.Line(a.EmployeeNumber, a.EmployeeName, a.DepartmentName,
            a.PresentDays.ToString(), a.LateDays.ToString(), a.AbsentDays.ToString(), a.RemoteDays.ToString(), a.TotalHours.ToString("0.##"))));
        return CsvFile(lines, $"attendance-{start:yyyyMMdd}-{end:yyyyMMdd}");
    }

    private FileContentResult CsvFile(IEnumerable<string> lines, string baseName)
        => File(Csv.ToBytes(lines), "text/csv", $"{baseName}-{DateTime.UtcNow:yyyyMMdd-HHmm}.csv");
}
