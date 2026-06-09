using Anela.Heblo.Domain.Features.Authorization;
using Anela.Heblo.Domain.Features.Authorization.Entities;
using Anela.Heblo.Persistence;
using Anela.Heblo.Persistence.Features.Authorization;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Anela.Heblo.Tests.Authorization;

public class AuthorizationSeederTests
{
    private static ApplicationDbContext NewDb() =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"seed_{Guid.NewGuid()}").Options);

    [Fact]
    public async Task Seed_CreatesAllGroupsFromAccessMatrix()
    {
        await using var db = NewDb();
        await AuthorizationSeeder.SeedAsync(db, default);

        var groups = await db.PermissionGroups.Include(g => g.Permissions).ToListAsync();
        groups.Should().HaveCount(AccessMatrix.Groups.Count);

        var spravce = groups.Single(g => g.Name == "Spravce");
        spravce.Permissions.Select(p => p.PermissionValue)
            .Should().BeEquivalentTo(AccessMatrix.AllRoleValues());
    }

    [Fact]
    public async Task Seed_IsIdempotent_NoDuplicatesOnSecondRun()
    {
        await using var db = NewDb();
        await AuthorizationSeeder.SeedAsync(db, default);
        await AuthorizationSeeder.SeedAsync(db, default);

        (await db.PermissionGroups.CountAsync()).Should().Be(AccessMatrix.Groups.Count);
    }

    [Fact]
    public async Task Seed_PrunesPermissionValuesNotInAccessMatrix()
    {
        await using var db = NewDb();
        await AuthorizationSeeder.SeedAsync(db, default);
        var grp = await db.PermissionGroups.FirstAsync(g => g.Name == "Nakupci");
        db.GroupPermissions.Add(new GroupPermission { GroupId = grp.Id, PermissionValue = "ghost.read" });
        await db.SaveChangesAsync();

        await AuthorizationSeeder.SeedAsync(db, default);

        (await db.GroupPermissions.AnyAsync(p => p.PermissionValue == "ghost.read"))
            .Should().BeFalse();
    }

    [Fact]
    public async Task Seed_PreservesPermissionsOfCustomGroups()
    {
        await using var db = NewDb();
        await AuthorizationSeeder.SeedAsync(db, default);

        var customGroupId = Guid.NewGuid();
        db.PermissionGroups.Add(new PermissionGroup
        {
            Id = customGroupId,
            Name = "CustomSales",
            Description = "Custom group",
            CreatedAt = DateTimeOffset.UtcNow,
            CreatedBy = "test",
        });
        db.GroupPermissions.Add(new GroupPermission { GroupId = customGroupId, PermissionValue = "products.catalog.read" });
        await db.SaveChangesAsync();

        await AuthorizationSeeder.SeedAsync(db, default);

        (await db.GroupPermissions.AnyAsync(p => p.GroupId == customGroupId && p.PermissionValue == "products.catalog.read"))
            .Should().BeTrue();
    }

    [Fact]
    public async Task Seed_DoesNotDeleteStalePermissionsFromCustomGroups()
    {
        await using var db = NewDb();
        await AuthorizationSeeder.SeedAsync(db, default);

        var customGroupId = Guid.NewGuid();
        db.PermissionGroups.Add(new PermissionGroup
        {
            Id = customGroupId,
            Name = "LegacyGroup",
            Description = "Custom group with old-format permission from before rename migration",
            CreatedAt = DateTimeOffset.UtcNow,
            CreatedBy = "test",
        });
        db.GroupPermissions.Add(new GroupPermission { GroupId = customGroupId, PermissionValue = "catalog.read" });
        await db.SaveChangesAsync();

        await AuthorizationSeeder.SeedAsync(db, default);

        (await db.GroupPermissions.AnyAsync(p => p.GroupId == customGroupId && p.PermissionValue == "catalog.read"))
            .Should().BeTrue("seeder must not clean up permissions that belong to custom groups");
    }
}
