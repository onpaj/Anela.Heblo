using Anela.Heblo.Domain.Features.Authorization;
using Anela.Heblo.Domain.Features.Authorization.Entities;
using Microsoft.EntityFrameworkCore;

namespace Anela.Heblo.Persistence.Features.Authorization;

public class AuthorizationRepository : IAuthorizationRepository
{
    private readonly ApplicationDbContext _db;

    public AuthorizationRepository(ApplicationDbContext db) => _db = db;

    public Task<AppUser?> GetUserByObjectIdAsync(string entraObjectId, CancellationToken ct = default) =>
        _db.AppUsers.FirstOrDefaultAsync(u => u.EntraObjectId == entraObjectId, ct);

    public async Task<AppUser> AddUserAsync(AppUser user, CancellationToken ct = default)
    {
        await _db.AppUsers.AddAsync(user, ct);
        return user;
    }

    public Task<AppUser?> GetUserByIdAsync(Guid id, CancellationToken ct = default) =>
        _db.AppUsers.Include(u => u.UserGroups).FirstOrDefaultAsync(u => u.Id == id, ct);

    public async Task<List<AppUser>> GetAllUsersAsync(CancellationToken ct = default) =>
        await _db.AppUsers.AsNoTracking().Include(u => u.UserGroups).ToListAsync(ct);

    public async Task<List<AppUser>> GetActivePackingUsersAsync(CancellationToken ct = default) =>
        await _db.AppUsers.AsNoTracking()
            .Where(u => u.IsActive && u.CanPack)
            .OrderBy(u => u.DisplayName)
            .ToListAsync(ct);

    public async Task<List<PermissionGroup>> GetAllGroupsAsync(CancellationToken ct = default) =>
        await _db.PermissionGroups.AsNoTracking()
            .Include(g => g.Permissions).Include(g => g.Parents).Include(g => g.UserGroups)
            .ToListAsync(ct);

    public Task<PermissionGroup?> GetGroupByIdAsync(Guid id, CancellationToken ct = default) =>
        _db.PermissionGroups.Include(g => g.Permissions).Include(g => g.Parents)
            .FirstOrDefaultAsync(g => g.Id == id, ct);

    public Task<PermissionGroup?> GetGroupByNameAsync(string name, CancellationToken ct = default) =>
        _db.PermissionGroups.Include(g => g.Permissions).Include(g => g.Parents)
            .FirstOrDefaultAsync(g => g.Name == name, ct);

    public async Task<PermissionGroup> AddGroupAsync(PermissionGroup group, CancellationToken ct = default)
    {
        await _db.PermissionGroups.AddAsync(group, ct);
        return group;
    }

    public Task RemoveGroupAsync(PermissionGroup group, CancellationToken ct = default)
    {
        _db.PermissionGroups.Remove(group);
        return Task.CompletedTask;
    }

    public async Task<List<UserGroup>> GetUserGroupsAsync(Guid userId, CancellationToken ct = default) =>
        await _db.UserGroups.Where(ug => ug.UserId == userId).ToListAsync(ct);

    public async Task SetUserGroupsAsync(Guid userId, IEnumerable<Guid> groupIds, CancellationToken ct = default)
    {
        var existing = await _db.UserGroups.Where(ug => ug.UserId == userId).ToListAsync(ct);
        _db.UserGroups.RemoveRange(existing);
        foreach (var gid in groupIds.Distinct())
            _db.UserGroups.Add(new UserGroup { UserId = userId, GroupId = gid });
    }

    public async Task<(List<GroupPermission> Permissions, List<GroupParent> Parents)> GetGroupGraphAsync(CancellationToken ct = default)
    {
        var perms = await _db.GroupPermissions.AsNoTracking().ToListAsync(ct);
        var parents = await _db.GroupParents.AsNoTracking().ToListAsync(ct);
        return (perms, parents);
    }

    public async Task AddUserToGroupAsync(Guid userId, Guid groupId, CancellationToken ct = default)
    {
        var exists = await _db.UserGroups.AnyAsync(ug => ug.UserId == userId && ug.GroupId == groupId, ct);
        if (!exists)
            _db.UserGroups.Add(new UserGroup { UserId = userId, GroupId = groupId });
    }

    public Task<int> SaveChangesAsync(CancellationToken ct = default) => _db.SaveChangesAsync(ct);
}
