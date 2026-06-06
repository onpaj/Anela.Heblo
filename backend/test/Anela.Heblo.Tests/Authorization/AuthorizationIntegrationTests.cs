using System.Net;
using System.Net.Http.Json;
using Anela.Heblo.Application.Features.Authorization.UseCases.GetGroups;
using Anela.Heblo.Application.Features.Authorization.UseCases.GetMe;
using Anela.Heblo.Domain.Features.Authorization;
using Anela.Heblo.Tests.Common;
using FluentAssertions;
using Xunit;

namespace Anela.Heblo.Tests.Authorization;

public class AuthorizationIntegrationTests : IClassFixture<HebloWebApplicationFactory>
{
    private readonly HebloWebApplicationFactory _factory;
    public AuthorizationIntegrationTests(HebloWebApplicationFactory factory) => _factory = factory;

    [Fact]
    public async Task Me_UnderMockAuth_IsSuperUser_WithAllPermissions()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/auth/me");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var me = await response.Content.ReadFromJsonAsync<GetMeResponse>();
        me!.IsSuperUser.Should().BeTrue();
        me.Permissions.Should().Contain("catalog.read");
        me.Permissions.Should().Contain(AccessRoles.Base);
    }

    [Fact]
    public async Task AdminGroups_ReturnsSeededSystemGroups()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/admin/authorization/groups");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<GetGroupsResponse>();
        body!.Groups.Select(g => g.Name).Should().Contain("Spravce");
        body.Groups.Should().OnlyContain(g => g.IsSystem);
    }

    [Fact]
    public async Task SuperUser_CanCall_RoleGatedEndpoint()
    {
        var client = _factory.CreateClient();
        // Find any GET endpoint with [Authorize(Roles = ...)] that doesn't require path params
        // The catalog route requires AccessRoles.CatalogRead — super_user must pass via wildcard.
        // Try /api/admin/authorization/catalogue as it's our own role-gated endpoint.
        var response = await client.GetAsync("/api/admin/authorization/catalogue");
        response.StatusCode.Should().NotBe(HttpStatusCode.Forbidden);
        response.StatusCode.Should().NotBe(HttpStatusCode.Unauthorized);
    }
}
