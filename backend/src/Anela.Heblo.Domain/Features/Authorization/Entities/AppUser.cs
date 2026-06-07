namespace Anela.Heblo.Domain.Features.Authorization.Entities;

/// <summary>An application user, materialized from Entra claims on first login.</summary>
public class AppUser
{
    public Guid Id { get; set; }
    public string EntraObjectId { get; set; } = null!;
    public string Email { get; set; } = null!;
    public string DisplayName { get; set; } = null!;
    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? LastLoginAt { get; set; }

    public ICollection<UserGroup> UserGroups { get; set; } = new List<UserGroup>();
}
