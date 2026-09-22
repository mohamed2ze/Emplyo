using Emplyo.Domain.Enums;

namespace Emplyo.Application.Notifications;

public class NotificationDto
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string? Url { get; set; }
    public NotificationType Type { get; set; }
    public bool IsRead { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}

public interface INotificationQueryService
{
    Task<IReadOnlyList<NotificationDto>> GetForUserAsync(string userId, bool unreadOnly = false, int take = 30, CancellationToken ct = default);
    Task<int> GetUnreadCountAsync(string userId, CancellationToken ct = default);
    Task<bool> MarkReadAsync(int id, string userId, CancellationToken ct = default);
    Task MarkAllReadAsync(string userId, CancellationToken ct = default);
}
