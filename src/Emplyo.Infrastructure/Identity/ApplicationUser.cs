using Emplyo.Domain.Entities;
using Microsoft.AspNetCore.Identity;

namespace Emplyo.Infrastructure.Identity;

public class ApplicationUser : IdentityUser
{
    public string DisplayName { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? LastLoginUtc { get; set; }
    public string? ProfilePhotoPath { get; set; }

    public Employee? Employee { get; set; }
}
