using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.MarketingAds.Proposals.UseCases.UpdateAdAutonomySetting;

public class UpdateAdAutonomySettingResponse : BaseResponse
{
    public UpdateAdAutonomySettingResponse() { }

    public UpdateAdAutonomySettingResponse(ErrorCodes errorCode, Dictionary<string, string>? parameters = null)
        : base(errorCode, parameters) { }
}
