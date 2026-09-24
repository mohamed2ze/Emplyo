using System.Diagnostics;
using Emplyo.Application.Common.Interfaces;
using Emplyo.Application.Common.Models;
using Emplyo.Domain.Entities;
using Microsoft.Extensions.Logging;

namespace Emplyo.Infrastructure.Services;

public class AuditService : IAuditService
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService _currentUser;
    private readonly IDateTimeProvider _clock;
    private readonly ILogger<AuditService> _logger;

    public AuditService(IApplicationDbContext db, ICurrentUserService currentUser,
        IDateTimeProvider clock, ILogger<AuditService> logger)
    {
        _db = db;
        _currentUser = currentUser;
        _clock = clock;
        _logger = logger;
    }

    public async Task RecordAsync(AuditEntry entry, CancellationToken ct = default)
    {
        var log = new AuditLog
        {
            EventId = Guid.NewGuid(),
            ActorUserId = _currentUser.UserId,
            ActorName = _currentUser.IsAuthenticated ? _currentUser.DisplayName : "system",
            Action = Truncate(entry.Action, 120),
            TargetType = Truncate(entry.TargetType, 120),
            TargetId = TruncateNullable(entry.TargetId, 80),
            Outcome = entry.Outcome,
            CorrelationId = TruncateNullable(entry.CorrelationId ?? Activity.Current?.Id, 120),
            Metadata = TruncateNullable(entry.Metadata, 1000),
            TimestampUtc = _clock.UtcNow
        };

        try
        {
            _db.AuditLogs.Add(log);
            await _db.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            // Audit failures must never break the primary business operation.
            _logger.LogError(ex, "Failed to persist audit entry for action {Action}.", entry.Action);
        }
    }

    private static string Truncate(string value, int max)
        => value.Length <= max ? value : value[..max];

    private static string? TruncateNullable(string? value, int max)
        => string.IsNullOrEmpty(value) ? value : (value.Length <= max ? value : value[..max]);
}
