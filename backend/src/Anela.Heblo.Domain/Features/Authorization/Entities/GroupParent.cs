namespace Anela.Heblo.Domain.Features.Authorization.Entities;

/// <summary>Nesting edge: GroupId inherits the permissions of ParentGroupId.</summary>
public class GroupParent
{
    public Guid GroupId { get; set; }
    public Guid ParentGroupId { get; set; }

    public PermissionGroup Group { get; set; } = null!;
    public PermissionGroup ParentGroup { get; set; } = null!;
}
