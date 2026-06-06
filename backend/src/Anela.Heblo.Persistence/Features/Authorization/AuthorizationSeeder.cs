using Anela.Heblo.Domain.Features.Authorization;
using Anela.Heblo.Domain.Features.Authorization.Entities;
using Microsoft.EntityFrameworkCore;

namespace Anela.Heblo.Persistence.Features.Authorization;

public static class AuthorizationSeeder
{
    public static async Task SeedAsync(ApplicationDbContext db, CancellationToken ct)
    {
        var validPermissions = AccessMatrix.AllRoleValues().ToHashSet();

        var existing = await db.PermissionGroups
            .Include(g => g.Permissions)
            .ToListAsync(ct);

        foreach (var matrixGroup in AccessMatrix.Groups)
        {
            var group = existing.FirstOrDefault(g => g.Name == matrixGroup.Name);
            if (group is null)
            {
                group = new PermissionGroup
                {
                    Id = Guid.NewGuid(),
                    Name = matrixGroup.Name,
                    Description = "System group (managed in code)",
                    IsSystem = true,
                    CreatedAt = DateTimeOffset.UtcNow,
                    CreatedBy = "system",
                };
                db.PermissionGroups.Add(group);
            }
            else
            {
                group.IsSystem = true;
            }

            // Re-sync permissions: code is authoritative for system groups.
            var desired = matrixGroup.Roles.Where(validPermissions.Contains).ToHashSet();
            var current = group.Permissions.Select(p => p.PermissionValue).ToHashSet();

            foreach (var toAdd in desired.Except(current))
                group.Permissions.Add(new GroupPermission { GroupId = group.Id, PermissionValue = toAdd });

            foreach (var perm in group.Permissions.Where(p => !desired.Contains(p.PermissionValue)).ToList())
                group.Permissions.Remove(perm);
        }

        // Global prune: drop any GroupPermission whose value left AccessMatrix entirely.
        var orphans = await db.GroupPermissions
            .Where(p => !validPermissions.Contains(p.PermissionValue))
            .ToListAsync(ct);
        db.GroupPermissions.RemoveRange(orphans);

        await db.SaveChangesAsync(ct);
    }
}
