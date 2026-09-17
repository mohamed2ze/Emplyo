using Emplyo.Application.Common.Models;
using Emplyo.Domain.Enums;

namespace Emplyo.Application.Employees;

public interface IEmployeeService
{
    Task<PagedResult<EmployeeListItemDto>> GetPagedAsync(EmployeeFilter filter, CancellationToken ct = default);
    Task<EmployeeDetailDto?> GetByIdAsync(int id, CancellationToken ct = default);
    Task<EmployeeDetailDto?> GetByUserIdAsync(string userId, CancellationToken ct = default);
    Task<int> CreateAsync(EmployeeFormDto dto, CancellationToken ct = default);
    Task UpdateAsync(int id, EmployeeFormDto dto, CancellationToken ct = default);
    Task ChangeStatusAsync(int id, EmploymentStatus newStatus, CancellationToken ct = default);
    Task UpdatePhotoAsync(int id, string? storedFileName, CancellationToken ct = default);
    Task UpdateContactInfoAsync(int id, string? phoneNumber, string? addressLine, string? city, string? country,
        string? emergencyContactName, string? emergencyContactPhone, CancellationToken ct = default);
    Task<IReadOnlyList<ManagerOptionDto>> GetManagerOptionsAsync(int? excludeEmployeeId = null, CancellationToken ct = default);
    Task<bool> EmployeeNumberExistsAsync(string employeeNumber, int? excludeId = null, CancellationToken ct = default);
    Task<bool> WorkEmailExistsAsync(string workEmail, int? excludeId = null, CancellationToken ct = default);
    Task<DateOnly> GetHireDateAsync(int id, CancellationToken ct = default);
}
