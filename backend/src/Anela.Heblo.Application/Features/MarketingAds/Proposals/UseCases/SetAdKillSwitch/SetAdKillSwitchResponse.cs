using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.MarketingAds.Proposals.UseCases.SetAdKillSwitch;

public class SetAdKillSwitchResponse : BaseResponse
{
    public SetAdKillSwitchResponse() { }

    public SetAdKillSwitchResponse(ErrorCodes errorCode, Dictionary<string, string>? parameters = null)
        : base(errorCode, parameters) { }
}
