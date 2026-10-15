using System.Security.Claims;
using Anela.Heblo.API.Features.Users;
using Anela.Heblo.API.Infrastructure.Authentication;
using Anela.Heblo.Application.Features.MarketingAds.Proposals.Approval;
using Anela.Heblo.Domain.Features.Authorization;
using Anela.Heblo.Domain.Features.MarketingAds;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.MarketingAds.Proposals.Approval;

public class TokenToApprovalChannelTests
{
    private const string WebClientId = "87193df0-3128-44d2-8673-580e97631a07";
    private const string ApiClientId = "8b34be89-f86f-422f-af40-7dbcd30cb66a";
    private const string AgentClientId = "aaaaaaaa-0000-0000-0000-000000000001";
    private const string Bearer = "Bearer eyJ0eXAi.fake.token";

    private static readonly string ApproveWrite = AccessRoles.For(Feature.Marketing_AdApprovals, AccessLevel.Write);
    private static readonly string ApproveViaMcp = AccessRoles.For(Feature.Marketing_AdApprovalsViaMcp, AccessLevel.Read);

    private static ApprovalChannelResolution Resolve(
        IEnumerable<Claim> claims, IEnumerable<string> roles, string? authorizationHeader, string authenticationType = "TestAuth")
    {
        var allClaims = claims.Concat(roles.Select(r => new Claim("roles", r)));
        var principal = new ClaimsPrincipal(new ClaimsIdentity(allClaims, authenticationType, "name", "roles"));
        var httpContext = new DefaultHttpContext { User = principal };
        if (authorizationHeader is not null)
        {
            httpContext.Request.Headers.Authorization = authorizationHeader;
        }

        var accessor = new Mock<IHttpContextAccessor>();
        accessor.Setup(a => a.HttpContext).Returns(httpContext);
        var options = Options.Create(new ApprovalChannelOptions
        {
            WebClientIds = new List<string> { WebClientId },
            ApiClientId = ApiClientId,
        });
        return new ApprovalChannelResolver(new CurrentUserService(accessor.Object), options).Resolve();
    }

    private static IEnumerable<string> EveryPermissionAndSuperUser() =>
        AccessMatrix.AllRoleValues().Append(AccessRoles.Base).Append(AccessRoles.SuperUser);

    [Fact]
    public void WebSpaUserToken_IsWebChannel()
    {
        var result = Resolve(
            new[] { new Claim("azp", WebClientId), new Claim("scp", "access_as_user"), new Claim("oid", "u1") },
            new[] { ApproveWrite }, Bearer);

        result.Channel.Should().Be(ApprovalChannel.Web);
    }

    [Fact]
    public void ClaudeConnectorUserToken_IsMcpChannel()
    {
        var result = Resolve(
            new[] { new Claim("azp", ApiClientId), new Claim("scp", "access_as_user"), new Claim("oid", "u1") },
            new[] { ApproveWrite, ApproveViaMcp }, Bearer);

        result.Channel.Should().Be(ApprovalChannel.Mcp);
    }

    [Fact]
    public void V1UserTokenFromWebSpa_IsWebChannel()
    {
        var result = Resolve(
            new[] { new Claim("appid", WebClientId), new Claim("scp", "access_as_user"), new Claim("oid", "u1") },
            new[] { ApproveWrite }, Bearer);

        result.Channel.Should().Be(ApprovalChannel.Web);
    }

    [Fact]
    public void ServicePrincipalToken_WithEveryPermissionAndSuperUser_IsDenied()
    {
        var result = Resolve(
            new[] { new Claim("azp", AgentClientId), new Claim("oid", "agent-sp"), new Claim("roles", "Marketing.Agent") },
            EveryPermissionAndSuperUser(), Bearer);

        result.Allowed.Should().BeFalse();
    }

    [Fact]
    public void ServicePrincipalToken_SpoofingTheWebClientId_IsDenied()
    {
        var result = Resolve(
            new[] { new Claim("azp", WebClientId), new Claim("idtyp", "app"), new Claim("oid", "agent-sp") },
            EveryPermissionAndSuperUser(), Bearer);

        result.Allowed.Should().BeFalse();
    }

    [Fact]
    public void E2ECookieSession_WithSuperUser_IsDenied()
    {
        // R16: the E2E session is built from a service-principal token, so it is a service identity.
        var result = Resolve(
            new[] { new Claim("oid", "e2e-test-object-id"), new Claim("scp", "access_as_user") },
            EveryPermissionAndSuperUser(), authorizationHeader: null, E2ESessionService.AuthenticationType);

        result.Allowed.Should().BeFalse();
    }

    [Fact]
    public void WebAppCookieSession_IsWebChannel()
    {
        var result = Resolve(
            new[] { new Claim("oid", "u1"), new Claim("scp", "access_as_user") },
            new[] { ApproveWrite }, authorizationHeader: null, "Cookies");

        result.Channel.Should().Be(ApprovalChannel.Web);
    }
}
