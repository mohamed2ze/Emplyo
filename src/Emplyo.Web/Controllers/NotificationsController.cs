using Emplyo.Application.Common.Interfaces;
using Emplyo.Application.Notifications;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Emplyo.Web.Controllers;

[Authorize]
public class NotificationsController : Controller
{
    private readonly INotificationQueryService _notifications;
    private readonly ICurrentUserService _currentUser;

    public NotificationsController(INotificationQueryService notifications, ICurrentUserService currentUser)
    {
        _notifications = notifications;
        _currentUser = currentUser;
    }

    public async Task<IActionResult> Index(CancellationToken ct)
    {
        ViewData["Title"] = "Notifications";
        return View(await _notifications.GetForUserAsync(_currentUser.UserId!, false, 100, ct));
    }

    [HttpPost]
    public async Task<IActionResult> MarkRead(int id, CancellationToken ct)
    {
        var notification = (await _notifications.GetForUserAsync(_currentUser.UserId!, false, 100, ct)).FirstOrDefault(n => n.Id == id);
        await _notifications.MarkReadAsync(id, _currentUser.UserId!, ct);
        if (!string.IsNullOrEmpty(notification?.Url) && Url.IsLocalUrl(notification.Url))
            return Redirect(notification.Url);
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    public async Task<IActionResult> MarkAllRead(CancellationToken ct)
    {
        await _notifications.MarkAllReadAsync(_currentUser.UserId!, ct);
        TempData["Success"] = "All notifications marked as read.";
        return RedirectToAction(nameof(Index));
    }
}
