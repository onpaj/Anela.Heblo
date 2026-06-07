using Anela.Heblo.Domain.Features.Authorization.Entities;
using Anela.Heblo.Persistence;
using Anela.Heblo.Persistence.Features.Authorization;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Anela.Heblo.Tests.Authorization;

public class AddGroupMemberHandlerTests
{
    private static ApplicationDbContext NewDb() =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"addmember_{Guid.NewGuid()}").Options);

    [Fact]
    public async Task AddUserToGroupAsync_WhenNotMember_AddsMembership()
    {
        await using var db = NewDb();
        var userId = Guid.NewGuid();
        var groupId = Guid.NewGuid();
        var repo = new AuthorizationRepository(db);

        await repo.AddUserToGroupAsync(userId, groupId);
        await repo.SaveChangesAsync();

        db.UserGroups.Should().ContainSingle(ug => ug.UserId == userId && ug.GroupId == groupId);
    }

    [Fact]
    public async Task AddUserToGroupAsync_WhenAlreadyMember_IsIdempotent()
    {
        await using var db = NewDb();
        var userId = Guid.NewGuid();
        var groupId = Guid.NewGuid();
        db.UserGroups.Add(new UserGroup { UserId = userId, GroupId = groupId });
        await db.SaveChangesAsync();

        var repo = new AuthorizationRepository(db);
        await repo.AddUserToGroupAsync(userId, groupId);
        await repo.SaveChangesAsync();

        db.UserGroups.Count(ug => ug.UserId == userId && ug.GroupId == groupId).Should().Be(1);
    }
}
