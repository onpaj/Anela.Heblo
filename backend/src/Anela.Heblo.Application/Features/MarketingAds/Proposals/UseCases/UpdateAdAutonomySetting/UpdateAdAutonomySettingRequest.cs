using Anela.Heblo.Application.Features.MarketingAds.Contracts;
using Anela.Heblo.Domain.Features.MarketingAds;
using MediatR;

namespace Anela.Heblo.Application.Features.MarketingAds.Proposals.UseCases.UpdateAdAutonomySetting;

public class UpdateAdAutonomySettingRequest : IRequest<UpdateAdAutonomySettingResponse>
{
    public AdPlatform Platform { get; set; }
    public AdActionType ActionType { get; set; }
    /// <summary>AutoWithinLimits and Auto both auto-approve only proposals the limits evaluate as Allowed.</summary>
    public AdAutonomyMode Mode { get; set; }
}
