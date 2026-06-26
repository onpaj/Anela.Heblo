using Anela.Heblo.Application.Features.Authorization.UseCases.GetMe;
using Anela.Heblo.Domain.Features.Authorization;
using Anela.Heblo.Domain.Features.Users;
using FluentAssertions;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Authorization;

public class GetMeHandlerTests
{
    [Fact]
    public async Task Handle_SuperUser_ReturnsAllPermissionsAndIsSuperUser()
    {
        var currentUser = new Mock<ICurrentUserService>();
        currentUser.Setup(c => c.GetCurrentUser()).Returns(new CurrentUser("oid-s", "Sue", "s@x.cz", true));
        currentUser.Setup(c => c.IsInRole(AccessRoles.SuperUser)).Returns(true);
        var resolver = new Mock<IPermissionResolver>(MockBehavior.Strict);

        var handler = new GetMeHandler(currentUser.Object, resolver.Object);
        var result = await handler.Handle(new GetMeRequest(), default);

        result.IsSuperUser.Should().BeTrue();
        result.Permissions.Should().BeEquivalentTo(AccessMatrix.AllRoleValues().Append(AccessRoles.Base));
    }

    [Fact]
    public async Task Handle_RegularUser_ReturnsResolvedPermissionsAndGroups()
    {
        var currentUser = new Mock<ICurrentUserService>();
        currentUser.Setup(c => c.GetCurrentUser()).Returns(new CurrentUser("oid-1", "Al", "a@x.cz", true));
        currentUser.Setup(c => c.IsInRole(AccessRoles.SuperUser)).Returns(false);
        var resolver = new Mock<IPermissionResolver>();
        resolver.Setup(r => r.ResolveAsync("oid-1", "a@x.cz", "Al", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EffectivePermissions(false, new[] { "heblo_user", "products.catalog.read" }, new[] { "Marketer" }));

        var handler = new GetMeHandler(currentUser.Object, resolver.Object);
        var result = await handler.Handle(new GetMeRequest(), default);

        result.IsSuperUser.Should().BeFalse();
        result.Permissions.Should().BeEquivalentTo(new[] { "heblo_user", "products.catalog.read" });
        result.Groups.Should().BeEquivalentTo(new[] { "Marketer" });
        result.Email.Should().Be("a@x.cz");
    }
}
