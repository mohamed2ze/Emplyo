using Emplyo.Application.Common.Interfaces;
using Emplyo.Domain.Entities;
using Emplyo.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Emplyo.Application.Notifications;

public class NotificationService : INotificationService, INotificationQueryService
{
    private readonly IApplicationDbContext _db;
    private readonly IIdentityService _identity;
    private readonly IDateTimeProvider _clock;

    public NotificationService(IApplicationDbContext db, IIdentityService identity, IDateTimeProvider clock)
    {
        _db = db;
        _identity = identity;
        _clock = clock;
    }

    public async Task NotifyUserAsync(string userId, string title, string message, string? url = null,
        NotificationType type = NotificationType.General, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(userId)) return;
        _db.Notifications.Add(new Notification
        {
            UserId = userId, Title = title, Message = message, Url = url, Type = type,
            CreatedAtUtc = _clock.UtcNow
        });
        await _db.SaveChangesAsync(ct);
    }

    public async Task NotifyRoleAsync(string role, string title, string message, string? url = null,
        NotificationType type = NotificationType.General, CancellationToken ct = default)
    {
        var users = await _identity.GetUsersInRoleAsync(role, ct);
        if (users.Count == 0) return;

        var now = _clock.UtcNow;
        foreach (var user in users)
        {
            _db.Notifications.Add(new Notification
            {
                UserId = user.Id, Title = title, Message = message, Url = url, Type = type,
                CreatedAtUtc = now
            });
        }
        await _db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<NotificationDto>> GetForUserAsync(string userId, bool unreadOnly = false, int take = 30, CancellationToken ct = default)
    {
        var query = _db.Notifications.AsNoTracking().Where(n => n.UserId == userId);
        if (unreadOnly) query = query.Where(n => !n.IsRead);

        return await query.OrderByDescending(n => n.CreatedAtUtc).Take(take)
            .Select(n => new NotificationDto
            {
                Id = n.Id, Title = n.Title, Message = n.Message, Url = n.Url,
                Type = n.Type, IsRead = n.IsRead, CreatedAtUtc = n.CreatedAtUtc
            }).ToListAsync(ct);
    }

    public Task<int> GetUnreadCountAsync(string userId, CancellationToken ct = default)
        => _db.Notifications.CountAsync(n => n.UserId == userId && !n.IsRead, ct);

    public async Task<bool> MarkReadAsync(int id, string userId, CancellationToken ct = default)
    {
        var n = await _db.Notifications.FirstOrDefaultAsync(x => x.Id == id && x.UserId == userId, ct);
        if (n is null) return false;
        n.IsRead = true;
        await _db.SaveChangesAsync(ct);
        return true;
    }

    public async Task MarkAllReadAsync(string userId, CancellationToken ct = default)
    {
        var items = await _db.Notifications.Where(n => n.UserId == userId && !n.IsRead).ToListAsync(ct);
        foreach (var item in items) item.IsRead = true;
        await _db.SaveChangesAsync(ct);
    }
}
