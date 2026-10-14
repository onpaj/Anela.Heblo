using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.MarketingAds.Proposals.UseCases.UpdateAdLimitSetting;

public class UpdateAdLimitSettingResponse : BaseResponse
{
    public UpdateAdLimitSettingResponse() { }

    public UpdateAdLimitSettingResponse(ErrorCodes errorCode, Dictionary<string, string>? parameters = null)
        : base(errorCode, parameters) { }
}
