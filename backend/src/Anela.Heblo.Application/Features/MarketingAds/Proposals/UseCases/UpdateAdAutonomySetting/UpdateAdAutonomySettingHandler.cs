using Anela.Heblo.Application.Features.MarketingAds.Proposals.Approval;
using Anela.Heblo.Application.Features.MarketingAds.Proposals.Audit;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.MarketingAds;
using Anela.Heblo.Domain.Features.Users;
using MediatR;

namespace Anela.Heblo.Application.Features.MarketingAds.Proposals.UseCases.UpdateAdAutonomySetting;

public class UpdateAdAutonomySettingHandler : IRequestHandler<UpdateAdAutonomySettingRequest, UpdateAdAutonomySettingResponse>
{
    private readonly IAdGovernanceRepository _governance;
    private readonly IAdAuditLog _audit;
    private readonly IApprovalChannelResolver _channelResolver;
    private readonly ICurrentUserService _currentUser;
    private readonly TimeProvider _time;

    public UpdateAdAutonomySettingHandler(IAdGovernanceRepository governance, IAdAuditLog audit, IApprovalChannelResolver channelResolver,
        ICurrentUserService currentUser, TimeProvider time)
    {
        _governance = governance;
        _audit = audit;
        _channelResolver = channelResolver;
        _currentUser = currentUser;
        _time = time;
    }

    public async Task<UpdateAdAutonomySettingResponse> Handle(UpdateAdAutonomySettingRequest request, CancellationToken cancellationToken)
    {
        var check = AdWebAdminGuard.Check(_currentUser, _channelResolver);
        if (!check.IsAllowed) return new UpdateAdAutonomySettingResponse(check.ErrorCode!.Value, check.Params);
        if (!Enum.IsDefined(request.Platform) || !Enum.IsDefined(request.ActionType) || !Enum.IsDefined(request.Mode))
        {
            return new UpdateAdAutonomySettingResponse(ErrorCodes.MarketingAdsInvalidSetting);
        }

        var now = _time.GetUtcNow().UtcDateTime;
        var platform = request.Platform.ToString();
        var actionType = request.ActionType.ToString();
        var setting = await _governance.FindAutonomySettingAsync(platform, actionType, cancellationToken);
        var oldMode = setting?.Mode; // primitive copy taken before the tracked row is mutated
        if (setting is null)
        {
            setting = new AdAutonomySetting { Platform = platform, ActionType = actionType };
            _governance.AddAutonomySetting(setting);
        }
        setting.Mode = request.Mode;
        setting.UpdatedAt = now;
        setting.UpdatedBy = check.Principal!;

        _audit.Append(AdAuditEvents.ForSetting(AdAuditEventType.SettingChanged, check.Principal!, ApprovalChannel.Web,
            new { setting = "autonomy", platform, actionType, old = oldMode?.ToString(), @new = request.Mode.ToString() }, now));
        return await _governance.TrySaveChangesAsync(cancellationToken)
            ? new UpdateAdAutonomySettingResponse()
            : new UpdateAdAutonomySettingResponse(ErrorCodes.MarketingAdsConcurrentChange);
    }
}
