using Anela.Heblo.Domain.Features.Authorization;
using Anela.Heblo.Domain.Features.Authorization.Entities;
using Anela.Heblo.Persistence;
using Anela.Heblo.Persistence.Features.Authorization;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Anela.Heblo.Tests.Authorization;

public class JsonGroupSeederTests
{
    private static ApplicationDbContext NewDb() =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"jsonseed_{Guid.NewGuid()}").Options);

    private static SeedGroupEntry Group(string name, params string[] roles) =>
        new(name, roles);

    [Fact]
    public async Task AddMissing_CreatesGroupsFromList_WhenDbIsEmpty()
    {
        await using var db = NewDb();
        var seedGroups = new[]
        {
            Group("Spravce", "products.catalog.read", "products.catalog.write"),
            Group("Skladnik", "warehouse.logistics.read"),
        };

        await JsonGroupSeeder.AddMissingGroupsAsync(db, seedGroups, default);

        var groups = await db.PermissionGroups.Include(g => g.Permissions).ToListAsync();
        groups.Should().HaveCount(2);
        groups.Single(g => g.Name == "Spravce").Permissions
            .Select(p => p.PermissionValue)
            .Should().BeEquivalentTo(new[] { "products.catalog.read", "products.catalog.write" });
        groups.Single(g => g.Name == "Skladnik").Permissions
            .Select(p => p.PermissionValue)
            .Should().BeEquivalentTo(new[] { "warehouse.logistics.read" });
    }

    [Fact]
    public async Task AddMissing_LeavesExistingGroupsUntouched()
    {
        await using var db = NewDb();

        // Pre-seed a "Spravce" group with permissions that do NOT match the JSON seed.
        var customId = Guid.NewGuid();
        db.PermissionGroups.Add(new PermissionGroup
        {
            Id = customId,
            Name = "Spravce",
            Description = "edited by admin",
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-30),
            CreatedBy = "admin@example.com",
        });
        db.GroupPermissions.Add(new GroupPermission
        {
            GroupId = customId,
            PermissionValue = "marketing.article.read",
        });
        await db.SaveChangesAsync();

        var seedGroups = new[]
        {
            Group("Spravce", "products.catalog.read", "products.catalog.write"),
        };

        await JsonGroupSeeder.AddMissingGroupsAsync(db, seedGroups, default);

        var persisted = await db.PermissionGroups
            .Include(g => g.Permissions)
            .SingleAsync(g => g.Name == "Spravce");

        persisted.Id.Should().Be(customId);
        persisted.Description.Should().Be("edited by admin");
        persisted.CreatedBy.Should().Be("admin@example.com");
        persisted.Permissions.Select(p => p.PermissionValue)
            .Should().BeEquivalentTo(new[] { "marketing.article.read" });
    }

    [Fact]
    public async Task AddMissing_IsIdempotent()
    {
        await using var db = NewDb();
        var seedGroups = new[]
        {
            Group("Spravce", "products.catalog.read"),
            Group("Skladnik", "warehouse.logistics.read"),
        };

        await JsonGroupSeeder.AddMissingGroupsAsync(db, seedGroups, default);
        await JsonGroupSeeder.AddMissingGroupsAsync(db, seedGroups, default);

        (await db.PermissionGroups.CountAsync()).Should().Be(2);
        (await db.GroupPermissions.CountAsync()).Should().Be(2);
    }

    [Fact]
    public async Task ResetGroup_RestoresPermissionsToJsonDefinition_OverwritingExisting()
    {
        await using var db = NewDb();

        // Seed the group first
        var seed = Group("Spravce", "products.catalog.read", "products.catalog.write");
        await JsonGroupSeeder.AddMissingGroupsAsync(db, new[] { seed }, default);

        // Mutate it (simulate admin edit)
        var group = await db.PermissionGroups
            .Include(g => g.Permissions)
            .SingleAsync(g => g.Name == "Spravce");
        db.GroupPermissions.RemoveRange(group.Permissions);
        db.GroupPermissions.Add(new GroupPermission
        {
            GroupId = group.Id,
            PermissionValue = "drifted.permission.read",
        });
        await db.SaveChangesAsync();

        // Now reset to JSON
        await JsonGroupSeeder.ResetGroupAsync(db, seed, default);

        var reset = await db.PermissionGroups
            .Include(g => g.Permissions)
            .SingleAsync(g => g.Name == "Spravce");

        reset.Permissions.Select(p => p.PermissionValue)
            .Should().BeEquivalentTo(new[] { "products.catalog.read", "products.catalog.write" });
    }

    [Fact]
    public async Task ResetGroup_PreservesUserGroupMemberships()
    {
        await using var db = NewDb();
        var seed = Group("Spravce", "products.catalog.read");
        await JsonGroupSeeder.AddMissingGroupsAsync(db, new[] { seed }, default);

        var group = await db.PermissionGroups.SingleAsync(g => g.Name == "Spravce");
        var userId = Guid.NewGuid();
        db.AppUsers.Add(new AppUser
        {
            Id = userId,
            EntraObjectId = "abc-123",
            Email = "user@example.com",
            DisplayName = "Test User",
            CreatedAt = DateTimeOffset.UtcNow,
        });
        db.UserGroups.Add(new UserGroup { UserId = userId, GroupId = group.Id });
        await db.SaveChangesAsync();

        await JsonGroupSeeder.ResetGroupAsync(db, seed, default);

        (await db.UserGroups.AnyAsync(ug => ug.UserId == userId && ug.GroupId == group.Id))
            .Should().BeTrue("reset must not delete user-group memberships");
    }

    [Fact]
    public async Task ResetGroup_PreservesParentRelationships()
    {
        await using var db = NewDb();
        var spravceSeed = Group("Spravce", "products.catalog.read");
        var vedeniSeed = Group("Vedeni", "products.catalog.read");
        await JsonGroupSeeder.AddMissingGroupsAsync(db, new[] { spravceSeed, vedeniSeed }, default);

        var spravce = await db.PermissionGroups.SingleAsync(g => g.Name == "Spravce");
        var vedeni = await db.PermissionGroups.SingleAsync(g => g.Name == "Vedeni");
        db.GroupParents.Add(new GroupParent { GroupId = vedeni.Id, ParentGroupId = spravce.Id });
        await db.SaveChangesAsync();

        await JsonGroupSeeder.ResetGroupAsync(db, vedeniSeed, default);

        (await db.GroupParents.AnyAsync(p => p.GroupId == vedeni.Id && p.ParentGroupId == spravce.Id))
            .Should().BeTrue("reset must not delete group parent relationships");
    }

    [Fact]
    public async Task ResetGroup_CreatesGroupWhenMissing()
    {
        await using var db = NewDb();
        var seed = Group("NewGroup", "products.catalog.read");

        await JsonGroupSeeder.ResetGroupAsync(db, seed, default);

        var group = await db.PermissionGroups
            .Include(g => g.Permissions)
            .SingleAsync(g => g.Name == "NewGroup");
        group.Permissions.Select(p => p.PermissionValue)
            .Should().BeEquivalentTo(new[] { "products.catalog.read" });
    }
}
