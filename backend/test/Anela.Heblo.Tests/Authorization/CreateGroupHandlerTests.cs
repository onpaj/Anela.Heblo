using Anela.Heblo.Application.Features.Authorization.UseCases.CreateGroup;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Authorization;
using Anela.Heblo.Domain.Features.Authorization.Entities;
using Anela.Heblo.Domain.Features.Users;
using Anela.Heblo.Persistence;
using Anela.Heblo.Persistence.Features.Authorization;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Authorization;

public class CreateGroupHandlerTests
{
    private static ApplicationDbContext NewDb() =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"creategroup_{Guid.NewGuid()}").Options);

    private static CreateGroupHandler NewHandler(ApplicationDbContext db)
    {
        var currentUser = new Mock<ICurrentUserService>();
        currentUser.Setup(c => c.GetCurrentUser()).Returns(new CurrentUser("oid", "Admin", "admin@x.cz", true));
        return new CreateGroupHandler(new AuthorizationRepository(db), currentUser.Object);
    }

    [Fact]
    public async Task Handle_ValidGroup_PersistsWithPermissions()
    {
        await using var db = NewDb();
        var result = await NewHandler(db).Handle(new CreateGroupRequest
        {
            Name = "Custom",
            Description = "desc",
            Permissions = new() { "catalog.read", "journal.read" },
        }, default);

        result.Success.Should().BeTrue();
        var saved = await db.PermissionGroups.Include(g => g.Permissions).SingleAsync();
        saved.IsSystem.Should().BeFalse();
        saved.Permissions.Select(p => p.PermissionValue).Should().BeEquivalentTo(new[] { "catalog.read", "journal.read" });
    }

    [Fact]
    public async Task Handle_UnknownPermission_ReturnsInvalidPermission()
    {
        await using var db = NewDb();
        var result = await NewHandler(db).Handle(new CreateGroupRequest
        {
            Name = "Bad",
            Permissions = new() { "ghost.read" },
        }, default);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.AuthorizationInvalidPermission);
    }

    [Fact]
    public async Task Handle_DuplicateName_ReturnsDuplicate()
    {
        await using var db = NewDb();
        db.PermissionGroups.Add(new PermissionGroup { Id = Guid.NewGuid(), Name = "Dup", CreatedAt = DateTimeOffset.UtcNow });
        await db.SaveChangesAsync();

        var result = await NewHandler(db).Handle(new CreateGroupRequest { Name = "Dup", Permissions = new() }, default);

        result.ErrorCode.Should().Be(ErrorCodes.AuthorizationDuplicateGroupName);
    }

    [Fact]
    public async Task Handle_ParentThatExists_Succeeds()
    {
        await using var db = NewDb();
        var p = new PermissionGroup { Id = Guid.NewGuid(), Name = "P", CreatedAt = DateTimeOffset.UtcNow };
        db.PermissionGroups.Add(p);
        await db.SaveChangesAsync();
        var result = await NewHandler(db).Handle(new CreateGroupRequest
        {
            Name = "Child",
            Permissions = new(),
            ParentGroupIds = new() { p.Id },
        }, default);

        result.Success.Should().BeTrue();
    }
}
