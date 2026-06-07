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
            if (existing.Any(g => g.Name == matrixGroup.Name))
                continue;

            var group = new PermissionGroup
            {
                Id = Guid.NewGuid(),
                Name = matrixGroup.Name,
                Description = matrixGroup.Name,
                CreatedAt = DateTimeOffset.UtcNow,
                CreatedBy = "system",
            };
            foreach (var role in matrixGroup.Roles.Where(validPermissions.Contains))
                group.Permissions.Add(new GroupPermission { GroupId = group.Id, PermissionValue = role });
            db.PermissionGroups.Add(group);
        }

        // Global prune: drop any GroupPermission whose value left AccessMatrix entirely.
        var orphans = await db.GroupPermissions
            .Where(p => !validPermissions.Contains(p.PermissionValue))
            .ToListAsync(ct);
        db.GroupPermissions.RemoveRange(orphans);

        await db.SaveChangesAsync(ct);
    }
}
