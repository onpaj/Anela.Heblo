using Anela.Heblo.Domain.Features.Authorization;
using Anela.Heblo.Domain.Features.Authorization.Entities;
using Microsoft.Extensions.Caching.Memory;

namespace Anela.Heblo.Persistence.Features.Authorization;

public class PermissionResolver : IPermissionResolver
{
    private readonly IAuthorizationRepository _repo;
    private readonly IMemoryCache _cache;
    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(5);

    public PermissionResolver(IAuthorizationRepository repo, IMemoryCache cache)
    {
        _repo = repo;
        _cache = cache;
    }

    private static string CacheKey(string objectId) => $"perms:{objectId}";

    public async Task<EffectivePermissions> ResolveAsync(
        string entraObjectId, string? email, string? displayName, CancellationToken ct = default)
    {
        if (_cache.TryGetValue(CacheKey(entraObjectId), out EffectivePermissions? cached) && cached is not null)
            return cached;

        var user = await _repo.GetUserByObjectIdAsync(entraObjectId, ct);
        if (user is null)
        {
            user = new AppUser
            {
                Id = Guid.NewGuid(),
                EntraObjectId = entraObjectId,
                Email = email ?? entraObjectId,
                DisplayName = displayName ?? email ?? entraObjectId,
                IsActive = true,
                CreatedAt = DateTimeOffset.UtcNow,
                LastLoginAt = DateTimeOffset.UtcNow,
            };
            await _repo.AddUserAsync(user, ct);
            await _repo.SaveChangesAsync(ct);
        }

        EffectivePermissions result;
        if (!user.IsActive)
        {
            result = EffectivePermissions.Empty;
        }
        else
        {
            var userGroups = await _repo.GetUserGroupsAsync(user.Id, ct);
            var groupIds = userGroups.Select(ug => ug.GroupId).ToList();
            var (perms, parents) = await _repo.GetGroupGraphAsync(ct);

            var resolved = GroupClosure.Resolve(groupIds, perms, parents);
            var permissions = new HashSet<string>(resolved, StringComparer.Ordinal) { AccessRoles.Base };

            var allGroups = await _repo.GetAllGroupsAsync(ct);
            var groupNames = allGroups.Where(g => groupIds.Contains(g.Id)).Select(g => g.Name).ToArray();

            result = new EffectivePermissions(false, permissions.ToArray(), groupNames);
        }

        _cache.Set(CacheKey(entraObjectId), result, CacheTtl);
        return result;
    }

    public void InvalidateCache(string entraObjectId) => _cache.Remove(CacheKey(entraObjectId));
}
