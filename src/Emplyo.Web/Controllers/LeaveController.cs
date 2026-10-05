using Emplyo.Application.Common.Interfaces;
using Emplyo.Application.LeaveManagement;
using Emplyo.Web.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Emplyo.Web.Controllers;

[Authorize]
public class LeaveController : Controller
{
    private readonly ILeaveService _leave;
    private readonly ILeaveTypeService _leaveTypes;
    private readonly ICurrentUserService _currentUser;

    public LeaveController(ILeaveService leave, ILeaveTypeService leaveTypes, ICurrentUserService currentUser)
    {
        _leave = leave;
        _leaveTypes = leaveTypes;
        _currentUser = currentUser;
    }

    [Authorize(Policy = Policies.HrOrAdmin)]
    public async Task<IActionResult> Index([FromQuery] LeaveFilter filter, CancellationToken ct)
    {
        ViewData["Title"] = "Leave management";
        ViewBag.Filter = filter;
        ViewBag.LeaveTypes = await _leaveTypes.GetActiveEntitiesAsync(ct);
        ViewBag.PendingCount = await _leave.GetPendingCountAsync(null, ct);
        return View(await _leave.GetPagedAsync(filter, ct));
    }

    public async Task<IActionResult> My(CancellationToken ct)
    {
        ViewData["Title"] = "My leave";
        var employeeId = _currentUser.EmployeeId;
        if (!employeeId.HasValue)
        {
            TempData["Warning"] = "Your account is not linked to an employee record.";
            return View("~/Views/Home/NoEmployee.cshtml");
        }

        ViewBag.Balances = await _leave.GetBalancesAsync(employeeId.Value, null, ct);
        var result = await _leave.GetPagedAsync(new LeaveFilter { EmployeeId = employeeId.Value, PageSize = 50 }, ct);
        return View(result);
    }

    [HttpGet]
    public async Task<IActionResult> Create(CancellationToken ct)
    {
        ViewData["Title"] = "Request leave";
        if (!_currentUser.EmployeeId.HasValue)
        {
            TempData["Warning"] = "Your account is not linked to an employee record.";
            return View("~/Views/Home/NoEmployee.cshtml");
        }

        ViewBag.LeaveTypes = await _leaveTypes.GetActiveEntitiesAsync(ct);
        ViewBag.Balances = await _leave.GetBalancesAsync(_currentUser.EmployeeId.Value, null, ct);
        return View(new LeaveRequestFormDto());
    }

    [HttpPost]
    public async Task<IActionResult> Create(LeaveRequestFormDto model, CancellationToken ct)
    {
        ViewData["Title"] = "Request leave";
        if (!_currentUser.EmployeeId.HasValue)
        {
            TempData["Warning"] = "Your account is not linked to an employee record.";
            return View("~/Views/Home/NoEmployee.cshtml");
        }

        if (!ModelState.IsValid)
        {
            ViewBag.LeaveTypes = await _leaveTypes.GetActiveEntitiesAsync(ct);
            ViewBag.Balances = await _leave.GetBalancesAsync(_currentUser.EmployeeId.Value, null, ct);
            return View(model);
        }

        var id = await _leave.SubmitAsync(_currentUser.EmployeeId.Value, model, ct);
        TempData["Success"] = "Your leave request was submitted for approval.";
        return RedirectToAction(nameof(Details), new { id });
    }

    public async Task<IActionResult> Details(int id, CancellationToken ct)
    {
        var request = await _leave.GetByIdAsync(id, ct);
        if (request is null) return NotFound();

        var isStaff = User.IsInRole(Roles.Administrator) || User.IsInRole(Roles.HrManager);
        var isOwner = _currentUser.EmployeeId == request.EmployeeId;
        if (!isStaff && !isOwner) return Forbid();

        ViewData["Title"] = "Leave request";
        ViewBag.IsStaff = isStaff;
        ViewBag.IsOwner = isOwner;
        return View(request);
    }

    [Authorize(Policy = Policies.HrOrAdmin)]
    [HttpPost]
    public async Task<IActionResult> Approve(int id, LeaveDecisionDto decision, CancellationToken ct)
    {
        await _leave.ApproveAsync(id, decision, ct);
        TempData["Success"] = "Leave request approved.";
        return RedirectToAction(nameof(Details), new { id });
    }

    [Authorize(Policy = Policies.HrOrAdmin)]
    [HttpPost]
    public async Task<IActionResult> Reject(int id, LeaveDecisionDto decision, CancellationToken ct)
    {
        await _leave.RejectAsync(id, decision, ct);
        TempData["Success"] = "Leave request rejected.";
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost]
    public async Task<IActionResult> Cancel(int id, CancellationToken ct)
    {
        var isStaff = User.IsInRole(Roles.Administrator) || User.IsInRole(Roles.HrManager);
        await _leave.CancelAsync(id, _currentUser.EmployeeId, isStaff, ct);
        TempData["Success"] = "Leave request cancelled.";
        return RedirectToAction(nameof(Details), new { id });
    }
}
