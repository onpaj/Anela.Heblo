using Anela.Heblo.Domain.Features.Authorization;
using Anela.Heblo.Domain.Features.Authorization.Entities;
using Anela.Heblo.Persistence;
using Anela.Heblo.Persistence.Features.Authorization;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Xunit;

namespace Anela.Heblo.Tests.Authorization;

public class PermissionResolverTests
{
    private static ApplicationDbContext NewDb() =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"resolver_{Guid.NewGuid()}").Options);

    private static PermissionResolver NewResolver(ApplicationDbContext db) =>
        new(new AuthorizationRepository(db), new MemoryCache(new MemoryCacheOptions()));

    [Fact]
    public async Task Resolve_UnknownUser_MaterializesAppUser_WithHebloUserOnly()
    {
        await using var db = NewDb();
        var resolver = NewResolver(db);

        var result = await resolver.ResolveAsync("oid-1", "a@b.cz", "Alice");

        result.IsSuperUser.Should().BeFalse();
        result.Permissions.Should().BeEquivalentTo(new[] { "heblo_user" });
        (await db.AppUsers.AnyAsync(u => u.EntraObjectId == "oid-1")).Should().BeTrue();
    }

    [Fact]
    public async Task Resolve_ActiveUserInGroup_ReturnsGroupPermissionsPlusBase()
    {
        await using var db = NewDb();
        var group = new PermissionGroup { Id = Guid.NewGuid(), Name = "G", CreatedAt = DateTimeOffset.UtcNow };
        group.Permissions.Add(new GroupPermission { GroupId = group.Id, PermissionValue = "products.catalog.read" });
        db.PermissionGroups.Add(group);
        var user = new AppUser { Id = Guid.NewGuid(), EntraObjectId = "oid-2", Email = "x", DisplayName = "X", IsActive = true, CreatedAt = DateTimeOffset.UtcNow };
        user.UserGroups.Add(new UserGroup { UserId = user.Id, GroupId = group.Id });
        db.AppUsers.Add(user);
        await db.SaveChangesAsync();

        var result = await NewResolver(db).ResolveAsync("oid-2", "x", "X");

        result.Permissions.Should().BeEquivalentTo(new[] { "heblo_user", "products.catalog.read" });
        result.Groups.Should().BeEquivalentTo(new[] { "G" });
    }

    [Fact]
    public async Task Resolve_InactiveUser_ReturnsEmpty()
    {
        await using var db = NewDb();
        db.AppUsers.Add(new AppUser { Id = Guid.NewGuid(), EntraObjectId = "oid-3", Email = "x", DisplayName = "X", IsActive = false, CreatedAt = DateTimeOffset.UtcNow });
        await db.SaveChangesAsync();

        var result = await NewResolver(db).ResolveAsync("oid-3", "x", "X");

        result.Permissions.Should().BeEmpty();
        result.IsSuperUser.Should().BeFalse();
    }
}
