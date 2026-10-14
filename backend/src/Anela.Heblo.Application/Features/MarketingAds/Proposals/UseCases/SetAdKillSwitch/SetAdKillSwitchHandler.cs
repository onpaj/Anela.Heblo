using Anela.Heblo.Application.Features.MarketingAds.Proposals.Approval;
using Anela.Heblo.Application.Features.MarketingAds.Proposals.Audit;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.MarketingAds;
using Anela.Heblo.Domain.Features.Users;
using MediatR;

namespace Anela.Heblo.Application.Features.MarketingAds.Proposals.UseCases.SetAdKillSwitch;

public class SetAdKillSwitchHandler : IRequestHandler<SetAdKillSwitchRequest, SetAdKillSwitchResponse>
{
    private readonly IAdGovernanceRepository _governance;
    private readonly IAdAuditLog _audit;
    private readonly IApprovalChannelResolver _channelResolver;
    private readonly ICurrentUserService _currentUser;
    private readonly TimeProvider _time;

    public SetAdKillSwitchHandler(IAdGovernanceRepository governance, IAdAuditLog audit, IApprovalChannelResolver channelResolver,
        ICurrentUserService currentUser, TimeProvider time)
    {
        _governance = governance;
        _audit = audit;
        _channelResolver = channelResolver;
        _currentUser = currentUser;
        _time = time;
    }

    public async Task<SetAdKillSwitchResponse> Handle(SetAdKillSwitchRequest request, CancellationToken cancellationToken)
    {
        var check = AdWebAdminGuard.Check(_currentUser, _channelResolver);
        if (!check.IsAllowed) return new SetAdKillSwitchResponse(check.ErrorCode!.Value, check.Params);
        if (request.Platform is { } p && !Enum.IsDefined(p)) return new SetAdKillSwitchResponse(ErrorCodes.MarketingAdsInvalidSetting);

        var now = _time.GetUtcNow().UtcDateTime;
        var scope = request.Platform?.ToString() ?? AdKillSwitch.GlobalScope;
        var killSwitch = await _governance.FindKillSwitchAsync(scope, cancellationToken);
        bool? oldValue = killSwitch?.ExecutionEnabled; // primitive copy taken before the tracked row is mutated
        if (killSwitch is null)
        {
            killSwitch = new AdKillSwitch { Scope = scope };
            _governance.AddKillSwitch(killSwitch);
        }
        killSwitch.ExecutionEnabled = request.ExecutionEnabled;
        killSwitch.UpdatedAt = now;
        killSwitch.UpdatedBy = check.Principal!;

        _audit.Append(AdAuditEvents.ForSetting(AdAuditEventType.KillSwitchChanged, check.Principal!, ApprovalChannel.Web,
            new { scope, old = oldValue, @new = request.ExecutionEnabled }, now));
        return await _governance.TrySaveChangesAsync(cancellationToken)
            ? new SetAdKillSwitchResponse()
            : new SetAdKillSwitchResponse(ErrorCodes.MarketingAdsConcurrentChange);
    }
}
