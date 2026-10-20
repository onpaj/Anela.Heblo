using Anela.Heblo.Application.Features.MarketingAds.Contracts;
using MediatR;

namespace Anela.Heblo.Application.Features.MarketingAds.Proposals.UseCases.UpdateAdLimitSetting;

public class UpdateAdLimitSettingRequest : IRequest<UpdateAdLimitSettingResponse>
{
    public AdPlatform Platform { get; set; }
    public AdActionType ActionType { get; set; }
    public string Key { get; set; } = string.Empty;
    public int Value { get; set; }
}
