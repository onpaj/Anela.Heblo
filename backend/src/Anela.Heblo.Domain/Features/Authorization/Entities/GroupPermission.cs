namespace Anela.Heblo.Domain.Features.Authorization.Entities;

/// <summary>Grants one code-defined permission (AccessMatrix value) to a group.</summary>
public class GroupPermission
{
    public Guid GroupId { get; set; }
    public string PermissionValue { get; set; } = null!;

    public PermissionGroup Group { get; set; } = null!;
}
