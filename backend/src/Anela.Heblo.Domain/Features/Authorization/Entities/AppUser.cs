namespace Anela.Heblo.Domain.Features.Authorization.Entities;

/// <summary>An application user. Entra users are materialized from claims on first login;
/// Local users are login-less packing operators created via administration.</summary>
public class AppUser
{
    public Guid Id { get; set; }
    public string? EntraObjectId { get; set; }
    public string Email { get; set; } = null!;
    public string DisplayName { get; set; } = null!;
    public bool IsActive { get; set; } = true;
    public AppUserSource Source { get; set; } = AppUserSource.Entra;
    public bool CanPack { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? LastLoginAt { get; set; }

    public ICollection<UserGroup> UserGroups { get; set; } = new List<UserGroup>();
}
