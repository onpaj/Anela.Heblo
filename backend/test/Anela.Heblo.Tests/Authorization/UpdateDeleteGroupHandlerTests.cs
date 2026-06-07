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

    private static async Task<PermissionGroup> SeedGroup(ApplicationDbContext db)
    {
        var g = new PermissionGroup { Id = Guid.NewGuid(), Name = "G", CreatedAt = DateTimeOffset.UtcNow };
        g.Permissions.Add(new GroupPermission { GroupId = g.Id, PermissionValue = "catalog.read" });
        db.PermissionGroups.Add(g);
        await db.SaveChangesAsync();
        return g;
    }

    [Fact]
    public async Task Update_ReplacesPermissions()
    {
        await using var db = NewDb();
        var g = await SeedGroup(db);
        var handler = new UpdateGroupHandler(new AuthorizationRepository(db));

        var result = await handler.Handle(new UpdateGroupRequest
        {
            Id = g.Id,
            Name = "G2",
            Permissions = new() { "journal.read" },
            ParentGroupIds = new()
        }, default);

        result.Success.Should().BeTrue();
        var reloaded = await db.PermissionGroups.Include(x => x.Permissions).SingleAsync();
        reloaded.Name.Should().Be("G2");
        reloaded.Permissions.Select(p => p.PermissionValue).Should().BeEquivalentTo(new[] { "journal.read" });
    }

    [Fact]
    public async Task Update_NotFound_ReturnsNotFound()
    {
        await using var db = NewDb();
        var handler = new UpdateGroupHandler(new AuthorizationRepository(db));

        var result = await handler.Handle(new UpdateGroupRequest
        {
            Id = Guid.NewGuid(),
            Name = "X",
            Permissions = new(),
            ParentGroupIds = new()
        }, default);

        result.ErrorCode.Should().Be(ErrorCodes.AuthorizationGroupNotFound);
    }

    [Fact]
    public async Task Delete_Removes()
    {
        await using var db = NewDb();
        var g = await SeedGroup(db);
        var handler = new DeleteGroupHandler(new AuthorizationRepository(db));

        var result = await handler.Handle(new DeleteGroupRequest { Id = g.Id }, default);

        result.Success.Should().BeTrue();
        (await db.PermissionGroups.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Delete_NotFound_ReturnsNotFound()
    {
        await using var db = NewDb();
        var handler = new DeleteGroupHandler(new AuthorizationRepository(db));

        var result = await handler.Handle(new DeleteGroupRequest { Id = Guid.NewGuid() }, default);

        result.ErrorCode.Should().Be(ErrorCodes.AuthorizationGroupNotFound);
    }
}
