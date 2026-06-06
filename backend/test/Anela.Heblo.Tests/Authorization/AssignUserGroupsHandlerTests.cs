using Anela.Heblo.Application.Features.Authorization.UseCases.AssignUserGroups;
using Anela.Heblo.Application.Features.Authorization.UseCases.SetUserActive;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Authorization.Entities;
using Anela.Heblo.Persistence;
using Anela.Heblo.Persistence.Features.Authorization;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Anela.Heblo.Tests.Authorization;

public class AssignUserGroupsHandlerTests
{
    private static ApplicationDbContext NewDb() =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"assign_{Guid.NewGuid()}").Options);

    private static async Task<(AppUser user, PermissionGroup g1, PermissionGroup g2)> Seed(ApplicationDbContext db)
    {
        var g1 = new PermissionGroup { Id = Guid.NewGuid(), Name = "G1", CreatedAt = DateTimeOffset.UtcNow };
        var g2 = new PermissionGroup { Id = Guid.NewGuid(), Name = "G2", CreatedAt = DateTimeOffset.UtcNow };
        var user = new AppUser { Id = Guid.NewGuid(), EntraObjectId = "oid", Email = "u@x.cz", DisplayName = "U", IsActive = true, CreatedAt = DateTimeOffset.UtcNow };
        user.UserGroups.Add(new UserGroup { UserId = user.Id, GroupId = g1.Id });
        db.AddRange(g1, g2, user);
        await db.SaveChangesAsync();
        return (user, g1, g2);
    }

    [Fact]
    public async Task Assign_ReplacesUserGroups()
    {
        await using var db = NewDb();
        var (user, _, g2) = await Seed(db);
        var handler = new AssignUserGroupsHandler(new AuthorizationRepository(db));

        var result = await handler.Handle(new AssignUserGroupsRequest
        {
            UserId = user.Id, GroupIds = new() { g2.Id }
        }, default);

        result.Success.Should().BeTrue();
        var groups = await db.UserGroups.Where(ug => ug.UserId == user.Id).Select(ug => ug.GroupId).ToListAsync();
        groups.Should().BeEquivalentTo(new[] { g2.Id });
    }

    [Fact]
    public async Task Assign_UnknownUser_ReturnsNotFound()
    {
        await using var db = NewDb();
        var handler = new AssignUserGroupsHandler(new AuthorizationRepository(db));
        var result = await handler.Handle(new AssignUserGroupsRequest { UserId = Guid.NewGuid(), GroupIds = new() }, default);
        result.ErrorCode.Should().Be(ErrorCodes.AuthorizationUserNotFound);
    }

    [Fact]
    public async Task SetActive_TogglesFlag()
    {
        await using var db = NewDb();
        var (user, _, _) = await Seed(db);
        var handler = new SetUserActiveHandler(new AuthorizationRepository(db));

        var result = await handler.Handle(new SetUserActiveRequest { UserId = user.Id, IsActive = false }, default);

        result.Success.Should().BeTrue();
        (await db.AppUsers.SingleAsync(u => u.Id == user.Id)).IsActive.Should().BeFalse();
    }
}
