using Anela.Heblo.Application.Features.Authorization.UseCases.GetPermissionCatalogue;
using Anela.Heblo.Domain.Features.Authorization;
using FluentAssertions;
using Xunit;

namespace Anela.Heblo.Tests.Authorization;

public class GetPermissionCatalogueHandlerTests
{
    [Fact]
    public async Task Handle_ReturnsAllPermissionsAndSystemGroups()
    {
        var handler = new GetPermissionCatalogueHandler();
        var result = await handler.Handle(new GetPermissionCatalogueRequest(), default);

        result.Success.Should().BeTrue();
        result.Permissions.Should().BeEquivalentTo(AccessMatrix.AllRoleValues());
        result.SystemGroups.Select(g => g.Name).Should().BeEquivalentTo(AccessMatrix.Groups.Select(g => g.Name));
        result.Features.Should().Contain(f => f.Key == "Products_Catalog" && f.HasWrite);
    }
}
