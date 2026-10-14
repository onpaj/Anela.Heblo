using System.Security.Claims;
using Anela.Heblo.API.Features.Users;
using Anela.Heblo.API.Infrastructure.Authentication;
using Anela.Heblo.Domain.Features.Authorization;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Users;

public class CurrentUserServiceClientClaimsTests
{
    private const string WebClientId = "87193df0-3128-44d2-8673-580e97631a07";
    private const string ApiClientId = "8b34be89-f86f-422f-af40-7dbcd30cb66a";
    private const string AgentClientId = "aaaaaaaa-0000-0000-0000-000000000001";
    private const string Bearer = "Bearer eyJ0eXAi.fake.token";
    private const string MappedScopeClaim = "http://schemas.microsoft.com/identity/claims/scope";

    private static CurrentUserService CreateService(ClaimsPrincipal principal, string? authorizationHeader)
    {
        var httpContext = new DefaultHttpContext { User = principal };
        if (authorizationHeader is not null)
        {
            httpContext.Request.Headers.Authorization = authorizationHeader;
        }

        var accessor = new Mock<IHttpContextAccessor>();
        accessor.Setup(x => x.HttpContext).Returns(httpContext);
        return new CurrentUserService(accessor.Object);
    }

    private static ClaimsPrincipal Authenticated(params Claim[] claims) =>
        new(new ClaimsIdentity(claims, "TestAuth", "name", "roles"));

    [Fact]
    public void V2DelegatedToken_ExposesAzp_AndIsNotAppOnly()
    {
        var service = CreateService(
            Authenticated(new Claim("azp", WebClientId), new Claim("scp", "access_as_user")), Bearer);

        service.ClientAppId.Should().Be(WebClientId);
        service.IsAppOnly.Should().BeFalse();
        service.IsBearerToken.Should().BeTrue();
    }

    [Fact]
    public void V1DelegatedToken_FallsBackToAppId()
    {
        var service = CreateService(
            Authenticated(new Claim("appid", ApiClientId), new Claim("scp", "access_as_user")), Bearer);

        service.ClientAppId.Should().Be(ApiClientId);
        service.IsAppOnly.Should().BeFalse();
    }

    [Fact]
    public void AzpWins_WhenBothAzpAndAppIdArePresent()
    {
        var service = CreateService(
            Authenticated(new Claim("azp", WebClientId), new Claim("appid", ApiClientId), new Claim("scp", "x")), Bearer);

        service.ClientAppId.Should().Be(WebClientId);
    }

    [Fact]
    public void MappedScopeClaimType_CountsAsDelegated()
    {
        var service = CreateService(
            Authenticated(new Claim("azp", WebClientId), new Claim(MappedScopeClaim, "access_as_user")), Bearer);

        service.IsAppOnly.Should().BeFalse();
    }

    [Fact]
    public void ClientCredentialsToken_WithoutScp_IsAppOnly_EvenWithSuperUserRole()
    {
        var service = CreateService(
            Authenticated(
                new Claim("azp", AgentClientId),
                new Claim("oid", "agent-sp-oid"),
                new Claim("roles", "Marketing.Agent"),
                new Claim("roles", "super_user")),
            Bearer);

        service.IsAppOnly.Should().BeTrue();
        service.ClientAppId.Should().Be(AgentClientId);
    }

    [Fact]
    public void IdtypApp_IsAppOnly()
    {
        var service = CreateService(
            Authenticated(new Claim("azp", AgentClientId), new Claim("idtyp", "app")), Bearer);

        service.IsAppOnly.Should().BeTrue();
    }

    [Fact]
    public void CookieSession_WithoutScp_IsNeitherAppOnlyNorBearer()
    {
        // Web-app OIDC cookie / E2E cookie session: no Authorization header.
        var service = CreateService(Authenticated(new Claim("oid", "user-oid")), authorizationHeader: null);

        service.IsAppOnly.Should().BeFalse();
        service.IsBearerToken.Should().BeFalse();
        service.ClientAppId.Should().BeNull();
    }

    [Fact]
    public void UnauthenticatedRequest_WithBearerHeader_IsNeitherAppOnlyNorBearer()
    {
        var service = CreateService(new ClaimsPrincipal(new ClaimsIdentity()), Bearer);

        service.IsAppOnly.Should().BeFalse();
        service.IsBearerToken.Should().BeFalse();
    }

    [Fact]
    public void NonBearerAuthorizationScheme_IsNotBearer()
    {
        var service = CreateService(Authenticated(new Claim("oid", "user-oid")), "Basic dXNlcjpwYXNz");

        service.IsBearerToken.Should().BeFalse();
        service.IsAppOnly.Should().BeFalse();
    }

    private static ClaimsPrincipal E2ESessionPrincipal() =>
        new(new ClaimsIdentity(
            new E2ESessionService(NullLogger<E2ESessionService>.Instance).CreateSyntheticUserClaims("Staging"),
            E2ESessionService.AuthenticationType));

    [Fact]
    public void E2ECookieSession_IsAppOnly_EvenThoughItCarriesScpAndNoBearerHeader()
    {
        // Arrange — the E2E session (cookie, or the X-E2E-Test-Token header path, which builds the
        // same identity) is minted from a service-principal token (R16)
        var service = CreateService(E2ESessionPrincipal(), authorizationHeader: null);

        // Act + Assert
        service.IsAppOnly.Should().BeTrue();
        service.IsBearerToken.Should().BeFalse();
    }

    [Fact]
    public async Task E2ECookieSession_StaysAppOnly_AfterCookieRoundTripAndPermissionTransformation()
    {
        // Arrange — serialize the ticket exactly like the cookie handler, then run the claims transformation
        var ticket = new AuthenticationTicket(E2ESessionPrincipal(), "E2ETestCookies");
        var roundTripped = TicketSerializer.Default.Deserialize(TicketSerializer.Default.Serialize(ticket))!;
        var resolver = new Mock<IPermissionResolver>();
        resolver
            .Setup(r => r.ResolveAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EffectivePermissions(false, Array.Empty<string>(), Array.Empty<string>()));
        var principal = await new PermissionClaimsTransformation(resolver.Object).TransformAsync(roundTripped.Principal);

        // Act
        var service = CreateService(principal, authorizationHeader: null);

        // Assert
        principal.Identities.Should().Contain(i => i.AuthenticationType == E2ESessionService.AuthenticationType);
        principal.IsInRole(AccessRoles.SuperUser).Should().BeTrue();
        service.IsAppOnly.Should().BeTrue();
    }

    [Fact]
    public void WebAppCookieSession_WithScp_IsNotAppOnly()
    {
        var service = CreateService(
            Authenticated(new Claim("oid", "user-oid"), new Claim("scp", "access_as_user")), authorizationHeader: null);

        service.IsAppOnly.Should().BeFalse();
    }
}
