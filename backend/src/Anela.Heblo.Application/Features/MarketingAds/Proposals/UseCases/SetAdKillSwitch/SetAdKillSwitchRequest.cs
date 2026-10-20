using Anela.Heblo.Application.Features.MarketingAds.Contracts;
using MediatR;

namespace Anela.Heblo.Application.Features.MarketingAds.Proposals.UseCases.SetAdKillSwitch;

public class SetAdKillSwitchRequest : IRequest<SetAdKillSwitchResponse>
{
    /// <summary>null = the global switch; a platform = an override that can only disable that platform.</summary>
    public AdPlatform? Platform { get; set; }
    public bool ExecutionEnabled { get; set; }
}
