using Anela.Heblo.API.MCP.Tools;
using Anela.Heblo.Domain.Features.Authorization;
using Anela.Heblo.Domain.Features.Users;
using MediatR;
using Microsoft.Extensions.Logging;
using Moq;

namespace Anela.Heblo.Tests.MCP.Tools;

public abstract class MarketingAdsMcpToolsTestBase
{
    protected static readonly string AdsRead = AccessRoles.For(Feature.Marketing_Ads, AccessLevel.Read);
    protected static readonly string AdsWrite = AccessRoles.For(Feature.Marketing_Ads, AccessLevel.Write);
    protected static readonly string ApprovalsWrite = AccessRoles.For(Feature.Marketing_AdApprovals, AccessLevel.Write);

    protected readonly Mock<IMediator> Mediator = new();
    protected readonly Mock<ICurrentUserService> CurrentUser = new();
    protected readonly Mock<ILogger<MarketingAdsMcpTools>> Logger = new();
    protected readonly MarketingAdsMcpTools Tools;

    protected MarketingAdsMcpToolsTestBase()
    {
        // Default: caller holds every role. FORBIDDEN tests deny a specific role (later setup wins).
        CurrentUser.Setup(s => s.IsInRole(It.IsAny<string>())).Returns(true);
        Tools = new MarketingAdsMcpTools(Mediator.Object, CurrentUser.Object, Logger.Object);
    }

    protected void DenyRole(string role) => CurrentUser.Setup(s => s.IsInRole(role)).Returns(false);
}
