using Anela.Heblo.Application.Features.Authorization.UseCases.AddGroupMember;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Authorization;
using Anela.Heblo.Domain.Features.Authorization.Entities;
using Anela.Heblo.Persistence;
using Anela.Heblo.Persistence.Features.Authorization;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;
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

    // ── Helpers ─────────────────────────────────────────────────────────────────

    private static PermissionGroup MakeGroup(ApplicationDbContext db)
    {
        var g = new PermissionGroup { Id = Guid.NewGuid(), Name = "Test", CreatedAt = DateTimeOffset.UtcNow };
        db.PermissionGroups.Add(g);
        db.SaveChanges();
        return g;
    }

    private static (AddGroupMemberHandler Handler, Mock<IPermissionResolver> Resolver) NewHandler(ApplicationDbContext db)
    {
        var resolver = new Mock<IPermissionResolver>();
        return (new AddGroupMemberHandler(new AuthorizationRepository(db), resolver.Object), resolver);
    }

    // ── Handler tests ────────────────────────────────────────────────────────────

    [Fact]
    public async Task Handle_NewEntraUser_IsProvisionedAndAddedToGroup()
    {
        await using var db = NewDb();
        var group = MakeGroup(db);
        var (handler, _) = NewHandler(db);

        var result = await handler.Handle(new AddGroupMemberRequest
        {
            GroupId = group.Id,
            EntraObjectId = "entra-new",
            Email = "new@x.cz",
            DisplayName = "New User",
        }, default);

        result.Success.Should().BeTrue();
        result.User.Should().NotBeNull();
        result.User!.Email.Should().Be("new@x.cz");
        result.User.LastLoginAt.Should().BeNull();

        var user = await db.AppUsers.SingleAsync(u => u.EntraObjectId == "entra-new");
        user.DisplayName.Should().Be("New User");
        db.UserGroups.Should().ContainSingle(ug => ug.UserId == user.Id && ug.GroupId == group.Id);
    }

    [Fact]
    public async Task Handle_ExistingEntraUser_AddedToGroupWithoutDuplicateProvision()
    {
        await using var db = NewDb();
        var group = MakeGroup(db);
        var existingUser = new AppUser
        {
            Id = Guid.NewGuid(),
            EntraObjectId = "entra-existing",
            Email = "existing@x.cz",
            DisplayName = "Existing",
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
            LastLoginAt = DateTimeOffset.UtcNow.AddDays(-5),
        };
        db.AppUsers.Add(existingUser);
        await db.SaveChangesAsync();

        var (handler, _) = NewHandler(db);
        var result = await handler.Handle(new AddGroupMemberRequest
        {
            GroupId = group.Id,
            EntraObjectId = "entra-existing",
            Email = "existing@x.cz",
            DisplayName = "Existing",
        }, default);

        result.Success.Should().BeTrue();
        db.AppUsers.Count(u => u.EntraObjectId == "entra-existing").Should().Be(1);
        db.UserGroups.Should().ContainSingle(ug => ug.UserId == existingUser.Id && ug.GroupId == group.Id);
    }

    [Fact]
    public async Task Handle_ReAdd_IsIdempotent()
    {
        await using var db = NewDb();
        var group = MakeGroup(db);
        var (handler, _) = NewHandler(db);
        var req = new AddGroupMemberRequest
        {
            GroupId = group.Id,
            EntraObjectId = "entra-idem",
            Email = "idem@x.cz",
            DisplayName = "Idempotent",
        };

        await handler.Handle(req, default);
        var result = await handler.Handle(req, default);

        result.Success.Should().BeTrue();
        db.UserGroups.Count(ug => ug.GroupId == group.Id).Should().Be(1);
    }

    [Fact]
    public async Task Handle_GroupNotFound_ReturnsError()
    {
        await using var db = NewDb();
        var (handler, _) = NewHandler(db);

        var result = await handler.Handle(new AddGroupMemberRequest
        {
            GroupId = Guid.NewGuid(),
            EntraObjectId = "entra-x",
            Email = "x@x.cz",
            DisplayName = "X",
        }, default);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.AuthorizationGroupNotFound);
    }

    [Fact]
    public async Task Handle_CacheIsInvalidatedForNewUser()
    {
        await using var db = NewDb();
        var group = MakeGroup(db);
        var (handler, resolver) = NewHandler(db);

        await handler.Handle(new AddGroupMemberRequest
        {
            GroupId = group.Id,
            EntraObjectId = "entra-cache",
            Email = "cache@x.cz",
            DisplayName = "Cache Test",
        }, default);

        resolver.Verify(r => r.InvalidateCache("entra-cache"), Times.Once);
    }
}
