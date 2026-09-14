using Emplyo.Domain.Enums;

namespace Emplyo.Domain.Entities;

public class AuditLog
{
    public long Id { get; set; }
    public Guid EventId { get; set; } = Guid.NewGuid();
    public string? ActorUserId { get; set; }
    public string ActorName { get; set; } = "system";
    public string Action { get; set; } = string.Empty;
    public string TargetType { get; set; } = string.Empty;
    public string? TargetId { get; set; }
    public AuditOutcome Outcome { get; set; } = AuditOutcome.Success;
    public string? CorrelationId { get; set; }
    public string? Metadata { get; set; }
    public DateTime TimestampUtc { get; set; } = DateTime.UtcNow;
}

public class AppSetting
{
    public int Id { get; set; }
    public string Key { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public string? Description { get; set; }
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}
