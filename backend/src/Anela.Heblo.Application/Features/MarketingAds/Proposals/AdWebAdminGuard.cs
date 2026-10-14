using Anela.Heblo.Application.Features.MarketingAds.Proposals.Approval;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Authorization;
using Anela.Heblo.Domain.Features.MarketingAds;
using Anela.Heblo.Domain.Features.Users;

namespace Anela.Heblo.Application.Features.MarketingAds.Proposals;

/// <summary>
/// Prologue of the web-only admin operations (revert, settings updates): an identified caller, an allowed
/// approval channel that is Web, and the Marketing_AdApprovals Admin permission. The controller's
/// [FeatureAuthorize] checks the same permission; this repeats it so the handler is safe on its own.
/// </summary>
public static class AdWebAdminGuard
{
    public const string WebOnlyReason = "This operation is available in the web UI only.";

    public static AdWebAdminCheck Check(ICurrentUserService currentUser, IApprovalChannelResolver channelResolver)
    {
        var principal = currentUser.GetCurrentUser().Id;
        if (string.IsNullOrWhiteSpace(principal)) return AdWebAdminCheck.Deny(ErrorCodes.Unauthorized);

        var resolution = channelResolver.Resolve();
        if (!resolution.Allowed)
        {
            return AdWebAdminCheck.Deny(ErrorCodes.MarketingAdsApprovalChannelDenied, AdProposalErrors.Reason(resolution.DenyReason));
        }
        if (resolution.Channel != ApprovalChannel.Web)
        {
            return AdWebAdminCheck.Deny(ErrorCodes.MarketingAdsApprovalChannelDenied, AdProposalErrors.Reason(WebOnlyReason));
        }

        return currentUser.IsInRole(AccessRoles.MarketingAdApprovalsAdmin)
            ? AdWebAdminCheck.Allow(principal)
            : AdWebAdminCheck.Deny(ErrorCodes.InsufficientPermissions);
    }
}

/// <summary>Outcome of <see cref="AdWebAdminGuard.Check"/>: the principal when allowed, otherwise the error to return.</summary>
public sealed class AdWebAdminCheck
{
    private AdWebAdminCheck(string? principal, ErrorCodes? errorCode, Dictionary<string, string>? parameters)
    {
        Principal = principal;
        ErrorCode = errorCode;
        Params = parameters;
    }

    public string? Principal { get; }
    public ErrorCodes? ErrorCode { get; }
    public Dictionary<string, string>? Params { get; }
    public bool IsAllowed => ErrorCode is null;

    public static AdWebAdminCheck Allow(string principal) => new(principal, null, null);

    public static AdWebAdminCheck Deny(ErrorCodes errorCode, Dictionary<string, string>? parameters = null) => new(null, errorCode, parameters);
}
