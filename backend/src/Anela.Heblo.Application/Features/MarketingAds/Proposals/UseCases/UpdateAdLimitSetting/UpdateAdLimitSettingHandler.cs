using Anela.Heblo.Application.Features.MarketingAds.Proposals.Approval;
using Anela.Heblo.Application.Features.MarketingAds.Proposals.Audit;
using Anela.Heblo.Application.Features.MarketingAds.Proposals.Limits;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.MarketingAds;
using Anela.Heblo.Domain.Features.Users;
using MediatR;

namespace Anela.Heblo.Application.Features.MarketingAds.Proposals.UseCases.UpdateAdLimitSetting;

public class UpdateAdLimitSettingHandler : IRequestHandler<UpdateAdLimitSettingRequest, UpdateAdLimitSettingResponse>
{
    private readonly IAdGovernanceRepository _governance;
    private readonly IAdAuditLog _audit;
    private readonly IApprovalChannelResolver _channelResolver;
    private readonly ICurrentUserService _currentUser;
    private readonly TimeProvider _time;

    public UpdateAdLimitSettingHandler(IAdGovernanceRepository governance, IAdAuditLog audit, IApprovalChannelResolver channelResolver,
        ICurrentUserService currentUser, TimeProvider time)
    {
        _governance = governance;
        _audit = audit;
        _channelResolver = channelResolver;
        _currentUser = currentUser;
        _time = time;
    }

    public async Task<UpdateAdLimitSettingResponse> Handle(UpdateAdLimitSettingRequest request, CancellationToken cancellationToken)
    {
        var check = AdWebAdminGuard.Check(_currentUser, _channelResolver);
        if (!check.IsAllowed) return new UpdateAdLimitSettingResponse(check.ErrorCode!.Value, check.Params);
        if (!IsValid(request)) return new UpdateAdLimitSettingResponse(ErrorCodes.MarketingAdsInvalidSetting);

        var now = _time.GetUtcNow().UtcDateTime;
        var platform = request.Platform.ToString();
        var actionType = request.ActionType.ToString();
        var setting = await _governance.FindLimitSettingAsync(platform, actionType, request.Key, cancellationToken);
        int? oldValue = setting?.Value; // primitive copy taken before the tracked row is mutated
        if (setting is null)
        {
            setting = new AdLimitSetting { Platform = platform, ActionType = actionType, Key = request.Key };
            _governance.AddLimitSetting(setting);
        }
        setting.Value = request.Value;
        setting.UpdatedAt = now;
        setting.UpdatedBy = check.Principal!;

        _audit.Append(AdAuditEvents.ForSetting(AdAuditEventType.SettingChanged, check.Principal!, ApprovalChannel.Web,
            new { setting = "limit", platform, actionType, key = request.Key, old = oldValue, @new = request.Value }, now));
        return await _governance.TrySaveChangesAsync(cancellationToken)
            ? new UpdateAdLimitSettingResponse()
            : new UpdateAdLimitSettingResponse(ErrorCodes.MarketingAdsConcurrentChange);
    }

    private static bool IsValid(UpdateAdLimitSettingRequest r) =>
        Enum.IsDefined(r.Platform) && Enum.IsDefined(r.ActionType)
        && AdLimitKeys.All.Contains(r.Key) && r.Value >= 0
        && (r.Key != AdLimitKeys.Enabled || r.Value is 0 or 1);
}
