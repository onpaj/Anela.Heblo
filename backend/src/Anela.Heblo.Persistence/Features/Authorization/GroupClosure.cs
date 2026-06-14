using Anela.Heblo.Domain.Features.Authorization.Entities;

namespace Anela.Heblo.Persistence.Features.Authorization;

/// <summary>Cycle-safe transitive closure of group nesting → union of permissions.</summary>
public static class GroupClosure
{
    public static IReadOnlyCollection<string> Resolve(
        IEnumerable<Guid> directGroupIds,
        IReadOnlyCollection<GroupPermission> allPermissions,
        IReadOnlyCollection<GroupParent> allParents)
    {
        var permsByGroup = allPermissions
            .GroupBy(p => p.GroupId)
            .ToDictionary(g => g.Key, g => g.Select(p => p.PermissionValue).ToArray());
        var parentsByGroup = allParents
            .GroupBy(p => p.GroupId)
            .ToDictionary(g => g.Key, g => g.Select(p => p.ParentGroupId).ToArray());

        var visited = new HashSet<Guid>();
        var queue = new Queue<Guid>(directGroupIds);
        var result = new HashSet<string>(StringComparer.Ordinal);

        while (queue.Count > 0)
        {
            var groupId = queue.Dequeue();
            if (!visited.Add(groupId)) continue; // already processed → cycle/diamond safe

            if (permsByGroup.TryGetValue(groupId, out var perms))
                foreach (var p in perms) result.Add(p);

            if (parentsByGroup.TryGetValue(groupId, out var parents))
                foreach (var parent in parents) queue.Enqueue(parent);
        }

        return result;
    }
}
