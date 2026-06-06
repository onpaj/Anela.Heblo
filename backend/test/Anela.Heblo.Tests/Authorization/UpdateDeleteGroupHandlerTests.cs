using Anela.Heblo.Application.Features.Authorization.UseCases.DeleteGroup;
using Anela.Heblo.Application.Features.Authorization.UseCases.UpdateGroup;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Authorization.Entities;
using Anela.Heblo.Persistence;
using Anela.Heblo.Persistence.Features.Authorization;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Anela.Heblo.Tests.Authorization;

public class UpdateDeleteGroupHandlerTests
{
    private static ApplicationDbContext NewDb() =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"upd_{Guid.NewGuid()}").Options);

    private static async Task<PermissionGroup> SeedGroup(ApplicationDbContext db, bool isSystem)
    {
        var g = new PermissionGroup { Id = Guid.NewGuid(), Name = "G", IsSystem = isSystem, CreatedAt = DateTimeOffset.UtcNow };
        g.Permissions.Add(new GroupPermission { GroupId = g.Id, PermissionValue = "catalog.read" });
        db.PermissionGroups.Add(g);
        await db.SaveChangesAsync();
        return g;
    }

    [Fact]
    public async Task Update_NonSystem_ReplacesPermissions()
    {
        await using var db = NewDb();
        var g = await SeedGroup(db, isSystem: false);
        var handler = new UpdateGroupHandler(new AuthorizationRepository(db));

        var result = await handler.Handle(new UpdateGroupRequest
        {
            Id = g.Id, Name = "G2", Permissions = new() { "journal.read" }, ParentGroupIds = new()
        }, default);

        result.Success.Should().BeTrue();
        var reloaded = await db.PermissionGroups.Include(x => x.Permissions).SingleAsync();
        reloaded.Name.Should().Be("G2");
        reloaded.Permissions.Select(p => p.PermissionValue).Should().BeEquivalentTo(new[] { "journal.read" });
    }

    [Fact]
    public async Task Update_SystemGroup_ReturnsImmutable()
    {
        await using var db = NewDb();
        var g = await SeedGroup(db, isSystem: true);
        var handler = new UpdateGroupHandler(new AuthorizationRepository(db));

        var result = await handler.Handle(new UpdateGroupRequest
        {
            Id = g.Id, Name = "X", Permissions = new(), ParentGroupIds = new()
        }, default);

        result.ErrorCode.Should().Be(ErrorCodes.AuthorizationSystemGroupImmutable);
    }

    [Fact]
    public async Task Delete_SystemGroup_ReturnsImmutable()
    {
        await using var db = NewDb();
        var g = await SeedGroup(db, isSystem: true);
        var handler = new DeleteGroupHandler(new AuthorizationRepository(db));

        var result = await handler.Handle(new DeleteGroupRequest { Id = g.Id }, default);

        result.ErrorCode.Should().Be(ErrorCodes.AuthorizationSystemGroupImmutable);
        (await db.PermissionGroups.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task Delete_NonSystem_Removes()
    {
        await using var db = NewDb();
        var g = await SeedGroup(db, isSystem: false);
        var handler = new DeleteGroupHandler(new AuthorizationRepository(db));

        var result = await handler.Handle(new DeleteGroupRequest { Id = g.Id }, default);

        result.Success.Should().BeTrue();
        (await db.PermissionGroups.CountAsync()).Should().Be(0);
    }
}
