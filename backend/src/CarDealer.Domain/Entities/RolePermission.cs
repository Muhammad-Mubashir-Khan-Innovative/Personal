namespace CarDealer.Domain.Entities;

/// <summary>
/// Join between a role and a permission. Composite key (RoleId, PermissionId).
/// </summary>
public class RolePermission
{
    public long RoleId { get; set; }

    public long PermissionId { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public Role Role { get; set; } = null!;

    public Permission Permission { get; set; } = null!;
}
