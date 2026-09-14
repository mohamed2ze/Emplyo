namespace Emplyo.Domain.Entities;

public class Department
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public int? DepartmentHeadId { get; set; }
    public Employee? DepartmentHead { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;

    public ICollection<Employee> Employees { get; set; } = new List<Employee>();
    public ICollection<Position> Positions { get; set; } = new List<Position>();
}
