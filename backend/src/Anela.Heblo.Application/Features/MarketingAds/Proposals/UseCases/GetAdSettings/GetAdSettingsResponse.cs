using Anela.Heblo.Application.Features.MarketingAds.Proposals.Dtos;
using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.MarketingAds.Proposals.UseCases.GetAdSettings;

public class GetAdSettingsResponse : BaseResponse
{
    public List<AdAutonomySettingDto> Autonomy { get; set; } = new();
    public List<AdLimitSettingDto> Limits { get; set; } = new();
    public List<AdKillSwitchDto> KillSwitches { get; set; } = new();
    public bool IsExecutionEnabledGlobally { get; set; }
    public List<AdApprovalRateDto> ApprovalRates { get; set; } = new();

    public GetAdSettingsResponse() { }

    public GetAdSettingsResponse(ErrorCodes errorCode, Dictionary<string, string>? parameters = null)
        : base(errorCode, parameters) { }
}
