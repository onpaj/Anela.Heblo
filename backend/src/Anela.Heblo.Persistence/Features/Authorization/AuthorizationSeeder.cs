using Anela.Heblo.Domain.Features.Authorization;
using Anela.Heblo.Domain.Features.Authorization.Entities;
using Microsoft.EntityFrameworkCore;

namespace Anela.Heblo.Persistence.Features.Authorization;

public static class AuthorizationSeeder
{
    public static async Task SeedAsync(ApplicationDbContext db, CancellationToken ct)
    {
        var validPermissions = AccessMatrix.AllPermissionStrings().ToHashSet();

        var existing = await db.PermissionGroups
            .Include(g => g.Permissions)
            .ToListAsync(ct);
        var existingByName = existing.ToDictionary(g => g.Name);

        foreach (var matrixGroup in AccessMatrix.Groups)
        {
            if (!existingByName.TryGetValue(matrixGroup.Name, out var group))
            {
                group = new PermissionGroup
                {
                    Id = Guid.NewGuid(),
                    Name = matrixGroup.Name,
                    Description = matrixGroup.Name,
                    CreatedAt = DateTimeOffset.UtcNow,
                    CreatedBy = "system",
                };
                db.PermissionGroups.Add(group);
            }

            var desired = matrixGroup.Roles.Where(validPermissions.Contains).ToHashSet();
            var current = group.Permissions.Select(p => p.PermissionValue).ToHashSet();

            foreach (var add in desired.Except(current))
                group.Permissions.Add(new GroupPermission { GroupId = group.Id, PermissionValue = add });

            foreach (var remove in group.Permissions.Where(p => !desired.Contains(p.PermissionValue)).ToList())
                group.Permissions.Remove(remove);
        }

        var seededGroupIds = AccessMatrix.Groups
            .Select(g => g.Name)
            .Where(existingByName.ContainsKey)
            .Select(name => existingByName[name].Id)
            .ToHashSet();

        var orphans = await db.GroupPermissions
            .Where(p => seededGroupIds.Contains(p.GroupId) && !validPermissions.Contains(p.PermissionValue))
            .ToListAsync(ct);
        db.GroupPermissions.RemoveRange(orphans);

        await db.SaveChangesAsync(ct);
    }
}
