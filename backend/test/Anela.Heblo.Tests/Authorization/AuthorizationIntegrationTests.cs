using System.Net;
using System.Net.Http.Json;
using Anela.Heblo.Application.Features.Authorization.UseCases.AddGroupMember;
using Anela.Heblo.Application.Features.Authorization.UseCases.GetEntraAccessUsers;
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
        me.Permissions.Should().Contain("products.catalog.read");
        me.Permissions.Should().Contain(AccessRoles.Base);
    }

    [Fact(Skip = "Groups are now seeded on-demand via JsonGroupSeeder in phase 5, not at startup")]
    public async Task AdminGroups_ReturnsSeededGroups()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/admin/authorization/groups");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<GetGroupsResponse>();
        body!.Groups.Select(g => g.Name).Should().Contain("Spravce");
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

    [Fact]
    public async Task GetEntraUsers_ReturnsOkWithEmptyList()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/admin/authorization/entra-users");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<GetEntraAccessUsersResponse>();
        body!.Success.Should().BeTrue();
        body.Users.Should().BeEmpty();
    }

    [Fact]
    public async Task AddGroupMember_GroupNotFound_ReturnsNotFound()
    {
        var client = _factory.CreateClient();
        var nonExistentId = Guid.NewGuid();

        var response = await client.PostAsJsonAsync(
            $"/api/admin/authorization/groups/{nonExistentId}/members",
            new { entraObjectId = "obj-1", email = "x@x.cz", displayName = "X" });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task AddGroupMember_ExistingGroup_ProvisionsMemberAndReturnsOk()
    {
        var client = _factory.CreateClient();

        var groupName = $"EntraIntegrationTestGroup_{Guid.NewGuid():N}";

        // Create a group
        var createResp = await client.PostAsJsonAsync(
            "/api/admin/authorization/groups",
            new { name = groupName, permissions = new string[] { } });
        createResp.EnsureSuccessStatusCode();

        // Get the group id
        var groupsResp = await client.GetFromJsonAsync<GetGroupsResponse>("/api/admin/authorization/groups");
        var group = groupsResp!.Groups.First(g => g.Name == groupName);

        var response = await client.PostAsJsonAsync(
            $"/api/admin/authorization/groups/{group.Id}/members",
            new { entraObjectId = "entra-integration-test", email = "int@x.cz", displayName = "Integration User" });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<AddGroupMemberResponse>();
        body!.Success.Should().BeTrue();
        body.User!.Email.Should().Be("int@x.cz");
        body.User.LastLoginAt.Should().BeNull();
    }
}
