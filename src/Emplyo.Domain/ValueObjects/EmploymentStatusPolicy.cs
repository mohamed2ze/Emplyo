using Emplyo.Domain.Enums;

namespace Emplyo.Domain.ValueObjects;

/// <summary>
/// Encodes the valid employment status transitions for an employee record.
/// Terminated -> Active is permitted to support re-hiring while preserving history.
/// </summary>
public static class EmploymentStatusPolicy
{
    private static readonly Dictionary<EmploymentStatus, EmploymentStatus[]> Transitions = new()
    {
        [EmploymentStatus.Pending] = new[] { EmploymentStatus.Active, EmploymentStatus.Suspended, EmploymentStatus.Terminated },
        [EmploymentStatus.Active] = new[] { EmploymentStatus.OnLeave, EmploymentStatus.Suspended, EmploymentStatus.Terminated },
        [EmploymentStatus.OnLeave] = new[] { EmploymentStatus.Active, EmploymentStatus.Suspended, EmploymentStatus.Terminated },
        [EmploymentStatus.Suspended] = new[] { EmploymentStatus.Active, EmploymentStatus.Terminated },
        [EmploymentStatus.Terminated] = new[] { EmploymentStatus.Active }
    };

    public static bool CanTransition(EmploymentStatus from, EmploymentStatus to)
        => from == to || (Transitions.TryGetValue(from, out var allowed) && allowed.Contains(to));

    public static IReadOnlyList<EmploymentStatus> AllowedTargets(EmploymentStatus from)
        => Transitions.TryGetValue(from, out var allowed) ? allowed : Array.Empty<EmploymentStatus>();
}
