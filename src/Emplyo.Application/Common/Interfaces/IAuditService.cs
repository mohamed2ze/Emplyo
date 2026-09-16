using Emplyo.Application.Common.Models;

namespace Emplyo.Application.Common.Interfaces;

public interface IAuditService
{
    Task RecordAsync(AuditEntry entry, CancellationToken ct = default);
}

public interface INotificationService
{
    Task NotifyUserAsync(string userId, string title, string message, string? url = null,
        Emplyo.Domain.Enums.NotificationType type = Emplyo.Domain.Enums.NotificationType.General,
        CancellationToken ct = default);

    Task NotifyRoleAsync(string role, string title, string message, string? url = null,
        Emplyo.Domain.Enums.NotificationType type = Emplyo.Domain.Enums.NotificationType.General,
        CancellationToken ct = default);
}

public interface IFileStorageService
{
    /// <summary>Persists a file with a generated server-side name and returns the stored name.</summary>
    Task<string> SaveAsync(Stream content, string originalFileName, CancellationToken ct = default);

    Stream OpenRead(string storedFileName);

    void Delete(string storedFileName);

    bool Exists(string storedFileName);
}
