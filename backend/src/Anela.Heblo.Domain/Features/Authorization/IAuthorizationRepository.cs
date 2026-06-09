using Anela.Heblo.Domain.Features.Authorization.Entities;

namespace Anela.Heblo.Domain.Features.Authorization;

/// <summary>Data access for the Authorization slice (single ApplicationDbContext, ADR-001).</summary>
public interface IAuthorizationRepository
{
    // Users
    Task<AppUser?> GetUserByObjectIdAsync(string entraObjectId, CancellationToken ct = default);
    Task<AppUser> AddUserAsync(AppUser user, CancellationToken ct = default);
    Task<AppUser?> GetUserByIdAsync(Guid id, CancellationToken ct = default);
    Task<List<AppUser>> GetAllUsersAsync(CancellationToken ct = default);

    // Groups
    Task<List<PermissionGroup>> GetAllGroupsAsync(CancellationToken ct = default);
    Task<PermissionGroup?> GetGroupByIdAsync(Guid id, CancellationToken ct = default);
    Task<PermissionGroup?> GetGroupByNameAsync(string name, CancellationToken ct = default);
    Task<PermissionGroup> AddGroupAsync(PermissionGroup group, CancellationToken ct = default);
    Task RemoveGroupAsync(PermissionGroup group, CancellationToken ct = default);

    // Edges
    Task<List<UserGroup>> GetUserGroupsAsync(Guid userId, CancellationToken ct = default);
    Task SetUserGroupsAsync(Guid userId, IEnumerable<Guid> groupIds, CancellationToken ct = default);
    /// <summary>Inserts a UserGroup row for (userId, groupId) if one does not already exist (idempotent).</summary>
    Task AddUserToGroupAsync(Guid userId, Guid groupId, CancellationToken ct = default);

    Task<List<AppUser>> GetGroupMembersAsync(Guid groupId, CancellationToken ct = default);

    /// <summary>All group→permission and group→parent edges, for closure resolution.</summary>
    Task<(List<GroupPermission> Permissions, List<GroupParent> Parents)> GetGroupGraphAsync(CancellationToken ct = default);

    Task<int> SaveChangesAsync(CancellationToken ct = default);
}
