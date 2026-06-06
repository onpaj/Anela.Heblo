namespace Anela.Heblo.Domain.Features.Authorization.Entities;

/// <summary>A named bundle of permissions ("group"/"role"), optionally nesting other groups.</summary>
public class PermissionGroup
{
    public Guid Id { get; set; }
    public string Name { get; set; } = null!;
    public string? Description { get; set; }
    /// <summary>Seeded from AccessMatrix.Groups; read-only and re-synced on startup.</summary>
    public bool IsSystem { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public string? CreatedBy { get; set; }

    public ICollection<GroupPermission> Permissions { get; set; } = new List<GroupPermission>();
    public ICollection<GroupParent> Parents { get; set; } = new List<GroupParent>();
    public ICollection<UserGroup> UserGroups { get; set; } = new List<UserGroup>();
}
