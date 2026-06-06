namespace Anela.Heblo.Domain.Features.Authorization.Entities;

/// <summary>Assignment of a user to a group.</summary>
public class UserGroup
{
    public Guid UserId { get; set; }
    public Guid GroupId { get; set; }

    public AppUser User { get; set; } = null!;
    public PermissionGroup Group { get; set; } = null!;
}
