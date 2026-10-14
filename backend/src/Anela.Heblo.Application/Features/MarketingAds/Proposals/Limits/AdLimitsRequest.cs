using Anela.Heblo.Application.Features.MarketingAds.Contracts;
using Anela.Heblo.Application.Features.MarketingAds.Proposals.ReadModel;
using Anela.Heblo.Domain.Features.MarketingAds;

namespace Anela.Heblo.Application.Features.MarketingAds.Proposals.Limits;

/// <summary>Everything the limits engine needs, gathered by IAdLimitsContextBuilder (Task 7).</summary>
public sealed record AdLimitsRequest(
    AdPlatform Platform,
    string AccountExternalId,
    AdActionType ActionType,
    IReadOnlyList<AdAction> Actions,
    ApprovalChannel Channel,
    string? ApproverPrincipal,
    IReadOnlyCollection<string> PriorApproverPrincipals,
    bool? IsAccountManaged,
    int SucceededPausesLast7Days,
    IReadOnlyDictionary<string, AdGroupOccupancy> AdGroupOccupancy,
    IReadOnlyDictionary<string, int> Limits);
