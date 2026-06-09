using Anela.Heblo.Domain.Features.Authorization;
using Anela.Heblo.Domain.Features.Authorization.Entities;
using Microsoft.EntityFrameworkCore;

namespace Anela.Heblo.Persistence.Features.Authorization;

/// <summary>
/// On-demand bootstrap of permission groups from access-matrix.json's seedGroups list.
/// Insert-if-missing semantics: existing groups in the DB are not mutated. Use
/// <see cref="ResetGroupAsync"/> to explicitly restore a named group to its JSON
/// definition.
/// </summary>
public static class JsonGroupSeeder
{
    public static async Task AddMissingGroupsAsync(
        ApplicationDbContext db,
        IReadOnlyList<SeedGroupEntry> seedGroups,
        CancellationToken ct)
    {
        var existingNames = (await db.PermissionGroups.Select(g => g.Name).ToListAsync(ct))
            .ToHashSet();

        foreach (var seed in seedGroups.Where(s => !existingNames.Contains(s.Name)))
        {
            var group = new PermissionGroup
            {
                Id = Guid.NewGuid(),
                Name = seed.Name,
                Description = seed.Name,
                CreatedAt = DateTimeOffset.UtcNow,
            };
            foreach (var role in seed.Roles)
                group.Permissions.Add(new GroupPermission
                {
                    GroupId = group.Id,
                    PermissionValue = role,
                });
            db.PermissionGroups.Add(group);
        }

        await db.SaveChangesAsync(ct);
    }

    public static async Task ResetGroupAsync(
        ApplicationDbContext db,
        SeedGroupEntry seed,
        CancellationToken ct)
    {
        var group = await db.PermissionGroups
            .Include(g => g.Permissions)
            .FirstOrDefaultAsync(g => g.Name == seed.Name, ct);

        if (group is null)
        {
            group = new PermissionGroup
            {
                Id = Guid.NewGuid(),
                Name = seed.Name,
                Description = seed.Name,
                CreatedAt = DateTimeOffset.UtcNow,
            };
            db.PermissionGroups.Add(group);
        }
        else
        {
            db.GroupPermissions.RemoveRange(group.Permissions);
            group.Permissions.Clear();
        }

        foreach (var role in seed.Roles)
            group.Permissions.Add(new GroupPermission
            {
                GroupId = group.Id,
                PermissionValue = role,
            });

        await db.SaveChangesAsync(ct);
    }
}
