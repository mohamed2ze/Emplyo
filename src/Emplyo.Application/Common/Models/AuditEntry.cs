using Emplyo.Domain.Enums;

namespace Emplyo.Application.Common.Models;

public class AuditEntry
{
    public string Action { get; init; } = string.Empty;
    public string TargetType { get; init; } = string.Empty;
    public string? TargetId { get; init; }
    public AuditOutcome Outcome { get; init; } = AuditOutcome.Success;
    public string? Metadata { get; init; }
    public string? CorrelationId { get; init; }
}
