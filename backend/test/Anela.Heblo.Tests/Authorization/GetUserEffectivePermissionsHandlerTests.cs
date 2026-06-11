using Anela.Heblo.Application.Features.Authorization.UseCases.GetUserEffectivePermissions;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Authorization;
using Anela.Heblo.Domain.Features.Authorization.Entities;
using FluentAssertions;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Authorization;

public class GetUserEffectivePermissionsHandlerTests
{
    [Fact]
    public async Task Handle_UserNotFound_ReturnsAuthorizationUserNotFoundAndDoesNotLoadGraph()
    {
        var userId = Guid.NewGuid();
        var repo = new Mock<IAuthorizationRepository>();
        repo.Setup(r => r.GetUserByIdAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((AppUser?)null);

        var handler = new GetUserEffectivePermissionsHandler(repo.Object);
        var response = await handler.Handle(
            new GetUserEffectivePermissionsRequest { UserId = userId },
            CancellationToken.None);

        response.Success.Should().BeFalse();
        response.ErrorCode.Should().Be(ErrorCodes.AuthorizationUserNotFound);
        repo.Verify(r => r.GetGroupGraphAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_InactiveUser_ReturnsEmptyPermissionsAndDoesNotLoadGraph()
    {
        var userId = Guid.NewGuid();
        var groupId = Guid.NewGuid();
        var user = new AppUser
        {
            Id = userId,
            Email = "u@x.cz",
            DisplayName = "U",
            IsActive = false,
            UserGroups = new List<UserGroup>
            {
                new() { UserId = userId, GroupId = groupId }
            }
        };
        var repo = new Mock<IAuthorizationRepository>();
        repo.Setup(r => r.GetUserByIdAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);

        var handler = new GetUserEffectivePermissionsHandler(repo.Object);
        var response = await handler.Handle(
            new GetUserEffectivePermissionsRequest { UserId = userId },
            CancellationToken.None);

        response.Success.Should().BeTrue();
        response.ErrorCode.Should().BeNull();
        response.Permissions.Should().BeEmpty();
        response.Permissions.Should().NotContain(AccessRoles.Base);
        repo.Verify(r => r.GetGroupGraphAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_ActiveUser_ReturnsMergedDistinctSortedPermissionsIncludingBase()
    {
        var userId = Guid.NewGuid();
        var g1 = Guid.NewGuid();
        var g2 = Guid.NewGuid();
        var user = new AppUser
        {
            Id = userId,
            Email = "u@x.cz",
            DisplayName = "U",
            IsActive = true,
            UserGroups = new List<UserGroup>
            {
                new() { UserId = userId, GroupId = g1 }
            }
        };
        var perms = new List<GroupPermission>
        {
            new() { GroupId = g1, PermissionValue = "permB" },
            new() { GroupId = g1, PermissionValue = "permA" },
            new() { GroupId = g2, PermissionValue = "permC" }
        };
        var parents = new List<GroupParent>
        {
            new() { GroupId = g1, ParentGroupId = g2 }
        };

        var repo = new Mock<IAuthorizationRepository>();
        repo.Setup(r => r.GetUserByIdAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);
        repo.Setup(r => r.GetGroupGraphAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync((perms, parents));

        var handler = new GetUserEffectivePermissionsHandler(repo.Object);
        var response = await handler.Handle(
            new GetUserEffectivePermissionsRequest { UserId = userId },
            CancellationToken.None);

        response.Success.Should().BeTrue();
        response.ErrorCode.Should().BeNull();
        response.Permissions.Should().Equal(AccessRoles.Base, "permA", "permB", "permC");
    }

    [Fact]
    public async Task Handle_ActiveUser_WhenGroupAlsoGrantsBase_BaseAppearsOnlyOnce()
    {
        var userId = Guid.NewGuid();
        var g1 = Guid.NewGuid();
        var user = new AppUser
        {
            Id = userId,
            Email = "u@x.cz",
            DisplayName = "U",
            IsActive = true,
            UserGroups = new List<UserGroup>
            {
                new() { UserId = userId, GroupId = g1 }
            }
        };
        var perms = new List<GroupPermission>
        {
            new() { GroupId = g1, PermissionValue = AccessRoles.Base },
            new() { GroupId = g1, PermissionValue = "permX" }
        };
        var parents = new List<GroupParent>();

        var repo = new Mock<IAuthorizationRepository>();
        repo.Setup(r => r.GetUserByIdAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);
        repo.Setup(r => r.GetGroupGraphAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync((perms, parents));

        var handler = new GetUserEffectivePermissionsHandler(repo.Object);
        var response = await handler.Handle(
            new GetUserEffectivePermissionsRequest { UserId = userId },
            CancellationToken.None);

        response.Success.Should().BeTrue();
        response.Permissions.Should().Equal(AccessRoles.Base, "permX");
        response.Permissions.Count(p => p == AccessRoles.Base).Should().Be(1);
    }
}
