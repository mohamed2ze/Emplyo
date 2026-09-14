using Emplyo.Domain.Enums;

namespace Emplyo.Domain.Entities;

public class LeaveRequest
{
    public int Id { get; set; }
    public int EmployeeId { get; set; }
    public Employee Employee { get; set; } = null!;
    public int LeaveTypeId { get; set; }
    public LeaveType LeaveType { get; set; } = null!;

    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }
    public bool IsHalfDay { get; set; }
    public decimal TotalDays { get; set; }
    public string? Reason { get; set; }

    public LeaveRequestStatus Status { get; set; } = LeaveRequestStatus.Pending;
    public string? DecisionByUserId { get; set; }
    public string? DecisionByName { get; set; }
    public DateTime? DecisionAtUtc { get; set; }
    public string? DecisionComments { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
    public byte[]? RowVersion { get; set; }
}
