using Anela.Heblo.Application.Features.MarketingAds.Proposals.Approval;
using Anela.Heblo.Domain.Features.Authorization;
using Anela.Heblo.Domain.Features.MarketingAds;
using Anela.Heblo.Domain.Features.Users;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.MarketingAds.Proposals.Approval;

public class ApprovalChannelResolverTests
{
    private const string WebClient = "web-spa-client";
    private const string HebloMcpClient = "heblo-mcp-client";
    private const string ApiClient = "api-client";
    private const string AgentClient = "agent-client";
    private const string AllRoles = "*";

    private static readonly string ApproveWrite = AccessRoles.For(Feature.Marketing_AdApprovals, AccessLevel.Write);
    private static readonly string ApproveViaMcp = AccessRoles.For(Feature.Marketing_AdApprovalsViaMcp, AccessLevel.Read);

    public sealed record ChannelCase(
        string Name,
        bool IsAuthenticated,
        bool IsAppOnly,
        bool IsBearerToken,
        string? ClientAppId,
        string[] GrantedRoles,
        bool ExpectedAllowed,
        ApprovalChannel? ExpectedChannel)
    {
        public override string ToString() => Name;
    }

    public static IEnumerable<object[]> Cases() => new[]
    {
        new ChannelCase("web SPA token with approve write", true, false, true, WebClient, new[] { ApproveWrite }, true, ApprovalChannel.Web),
        new ChannelCase("web SPA token without approve write", true, false, true, WebClient, Array.Empty<string>(), false, null),
        new ChannelCase("web SPA token with only approve-via-MCP", true, false, true, WebClient, new[] { ApproveViaMcp }, false, null),
        new ChannelCase("Claude connector (API client id) with write + via-MCP", true, false, true, ApiClient, new[] { ApproveWrite, ApproveViaMcp }, true, ApprovalChannel.Mcp),
        new ChannelCase("Claude connector with write but no via-MCP", true, false, true, ApiClient, new[] { ApproveWrite }, false, null),
        new ChannelCase("Claude connector with via-MCP but no write", true, false, true, ApiClient, new[] { ApproveViaMcp }, false, null),
        new ChannelCase("HebloMCP client with write + via-MCP", true, false, true, HebloMcpClient, new[] { ApproveWrite, ApproveViaMcp }, true, ApprovalChannel.Mcp),
        new ChannelCase("service principal with all permissions still denied", true, true, true, AgentClient, new[] { AllRoles }, false, null),
        new ChannelCase("super_user role still denied for app-only on the web client id", true, true, true, WebClient, new[] { AllRoles }, false, null),
        new ChannelCase("super_user role still denied for app-only on the API client id", true, true, true, ApiClient, new[] { AllRoles }, false, null),
        new ChannelCase("cookie session with approve write", true, false, false, null, new[] { ApproveWrite }, true, ApprovalChannel.Web),
        new ChannelCase("cookie session without approve write", true, false, false, null, Array.Empty<string>(), false, null),
        new ChannelCase("bearer token from an unknown client with all permissions", true, false, true, "some-other-client", new[] { AllRoles }, false, null),
        new ChannelCase("bearer token without any client id", true, false, true, null, new[] { AllRoles }, false, null),
        new ChannelCase("unauthenticated caller", false, false, false, null, new[] { AllRoles }, false, null),
    }.Select(c => new object[] { c });

    private static Mock<ICurrentUserService> CurrentUser(ChannelCase c)
    {
        var mock = new Mock<ICurrentUserService>();
        mock.Setup(s => s.GetCurrentUser()).Returns(new CurrentUser("oid-1", "Someone", null, c.IsAuthenticated));
        mock.SetupGet(s => s.IsAppOnly).Returns(c.IsAppOnly);
        mock.SetupGet(s => s.IsBearerToken).Returns(c.IsBearerToken);
        mock.SetupGet(s => s.ClientAppId).Returns(c.ClientAppId);
        mock.Setup(s => s.IsInRole(It.IsAny<string>()))
            .Returns<string>(role => c.GrantedRoles.Contains(AllRoles) || c.GrantedRoles.Contains(role));
        return mock;
    }

    private static ApprovalChannelResolver CreateResolver(ICurrentUserService currentUser, ApprovalChannelOptions? options = null) =>
        new(currentUser, Options.Create(options ?? new ApprovalChannelOptions
        {
            WebClientIds = new List<string> { WebClient },
            McpClientIds = new List<string> { HebloMcpClient },
            ApiClientId = ApiClient,
        }));

    [Theory]
    [MemberData(nameof(Cases))]
    public void Resolve_AppliesChannelRules(ChannelCase c)
    {
        // Arrange
        var resolver = CreateResolver(CurrentUser(c).Object);

        // Act
        var result = resolver.Resolve();

        // Assert
        result.Allowed.Should().Be(c.ExpectedAllowed);
        if (c.ExpectedChannel is { } expectedChannel)
        {
            result.Channel.Should().Be(expectedChannel);
        }
        if (!c.ExpectedAllowed)
        {
            result.DenyReason.Should().NotBeNullOrWhiteSpace();
        }
    }

    [Fact]
    public void Resolve_DeniesAppOnly_WithoutEverConsultingRoles()
    {
        // Arrange
        var c = new ChannelCase("app-only", true, true, true, WebClient, new[] { AllRoles }, false, null);
        var currentUser = CurrentUser(c);

        // Act
        var result = CreateResolver(currentUser.Object).Resolve();

        // Assert
        result.Allowed.Should().BeFalse();
        currentUser.Verify(s => s.IsInRole(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public void Resolve_MatchesClientIdsIgnoringCaseAndWhitespace()
    {
        // Arrange — config overridden from Key Vault with odd casing, padding and a duplicate
        var c = new ChannelCase("padded", true, false, true, WebClient, new[] { ApproveWrite }, true, ApprovalChannel.Web);
        var options = new ApprovalChannelOptions
        {
            WebClientIds = new List<string> { "  WEB-SPA-CLIENT ", "web-spa-client", "" },
            McpClientIds = new List<string>(),
            ApiClientId = null,
        };

        // Act
        var result = CreateResolver(CurrentUser(c).Object, options).Resolve();

        // Assert
        result.Allowed.Should().BeTrue();
        result.Channel.Should().Be(ApprovalChannel.Web);
    }

    [Fact]
    public void Resolve_DeniesWebBearerApproval_WhenNoWebClientIdsConfigured_AndNamesTheClient()
    {
        // Arrange — production misconfiguration: the SPA id was never set
        var c = new ChannelCase("unconfigured", true, false, true, WebClient, new[] { AllRoles }, false, null);
        var options = new ApprovalChannelOptions { ApiClientId = ApiClient };

        // Act
        var result = CreateResolver(CurrentUser(c).Object, options).Resolve();

        // Assert
        result.Allowed.Should().BeFalse();
        result.DenyReason.Should().Contain(WebClient);
    }

    [Fact]
    public void Resolve_TreatsApiClientIdAsMcp_EvenWhenMcpListIsEmpty()
    {
        // Arrange
        var c = new ChannelCase("api default", true, false, true, ApiClient, new[] { ApproveWrite, ApproveViaMcp }, true, ApprovalChannel.Mcp);
        var options = new ApprovalChannelOptions { WebClientIds = new List<string> { WebClient }, ApiClientId = ApiClient };

        // Act
        var result = CreateResolver(CurrentUser(c).Object, options).Resolve();

        // Assert
        result.Channel.Should().Be(ApprovalChannel.Mcp);
    }
}
