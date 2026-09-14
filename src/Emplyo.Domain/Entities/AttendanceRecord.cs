using Emplyo.Domain.Enums;

namespace Emplyo.Domain.Entities;

public class AttendanceRecord
{
    public int Id { get; set; }
    public int EmployeeId { get; set; }
    public Employee Employee { get; set; } = null!;

    public DateOnly WorkDate { get; set; }
    public DateTime? ClockInUtc { get; set; }
    public DateTime? ClockOutUtc { get; set; }
    public AttendanceStatus Status { get; set; } = AttendanceStatus.Present;
    public string? Notes { get; set; }

    public string? CorrectedByUserId { get; set; }
    public DateTime? CorrectedAtUtc { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
    public byte[]? RowVersion { get; set; }
}
