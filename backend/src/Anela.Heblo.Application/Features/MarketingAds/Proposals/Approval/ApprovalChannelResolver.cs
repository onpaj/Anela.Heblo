using Anela.Heblo.Domain.Features.Authorization;
using Anela.Heblo.Domain.Features.MarketingAds;
using Anela.Heblo.Domain.Features.Users;
using Microsoft.Extensions.Options;

namespace Anela.Heblo.Application.Features.MarketingAds.Proposals.Approval;

/// <summary>Decides on which channel the current caller approves/rejects an ad proposal, or
/// refuses. Order matters: app-only callers are refused before any permission is looked at.</summary>
public sealed class ApprovalChannelResolver : IApprovalChannelResolver
{
    private static readonly string ApproveWriteRole = AccessRoles.For(Feature.Marketing_AdApprovals, AccessLevel.Write);
    private static readonly string ApproveViaMcpRole = AccessRoles.For(Feature.Marketing_AdApprovalsViaMcp, AccessLevel.Read);

    private readonly ICurrentUserService _currentUser;
    private readonly IReadOnlySet<string> _webClientIds;
    private readonly IReadOnlySet<string> _mcpClientIds;

    public ApprovalChannelResolver(ICurrentUserService currentUser, IOptions<ApprovalChannelOptions> options)
    {
        _currentUser = currentUser;
        var value = options.Value;
        _webClientIds = ToClientIdSet(value.WebClientIds);
        _mcpClientIds = ToClientIdSet(value.McpClientIds.Append(value.ApiClientId));
    }

    public ApprovalChannelResolution Resolve()
    {
        if (!_currentUser.GetCurrentUser().IsAuthenticated)
        {
            return Deny("The caller is not authenticated.");
        }

        if (_currentUser.IsAppOnly)
        {
            return Deny("Service identities (app-only tokens, the E2E test session) can never approve or reject ad proposals.");
        }

        if (!_currentUser.IsBearerToken)
        {
            return RequireWeb(); // cookie session: the web app's own login (the E2E session is app-only above)
        }

        var clientId = _currentUser.ClientAppId;
        if (clientId is null)
        {
            return Deny("The bearer token carries no client application id (azp/appid).");
        }

        // MCP before Web: a client id listed in both resolves to the stricter MCP channel.
        if (_mcpClientIds.Contains(clientId))
        {
            return RequireMcp();
        }

        if (_webClientIds.Contains(clientId))
        {
            return RequireWeb();
        }

        return Deny($"Client application '{clientId}' is not allowed to approve or reject ad proposals.");
    }

    private ApprovalChannelResolution RequireWeb() =>
        _currentUser.IsInRole(ApproveWriteRole)
            ? Allow(ApprovalChannel.Web)
            : Deny($"Approving ad proposals requires {ApproveWriteRole}.");

    private ApprovalChannelResolution RequireMcp()
    {
        if (!_currentUser.IsInRole(ApproveWriteRole))
        {
            return Deny($"Approving ad proposals requires {ApproveWriteRole}.");
        }

        return _currentUser.IsInRole(ApproveViaMcpRole)
            ? Allow(ApprovalChannel.Mcp)
            : Deny($"Approving ad proposals via Claude (MCP) requires {ApproveViaMcpRole}.");
    }

    private static ApprovalChannelResolution Allow(ApprovalChannel channel) => ApprovalChannelResolution.Allow(channel);

    private static ApprovalChannelResolution Deny(string reason) => ApprovalChannelResolution.Deny(reason);

    private static IReadOnlySet<string> ToClientIdSet(IEnumerable<string?> clientIds) =>
        clientIds
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => id!.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
}
