using Anela.Heblo.Application.Features.MarketingAds.Contracts;
using Anela.Heblo.Application.Features.MarketingAds.Proposals.Actions;
using Anela.Heblo.Application.Features.MarketingAds.Proposals.Limits;
using Anela.Heblo.Application.Features.MarketingAds.Proposals.ReadModel;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.MarketingAds;
using Anela.Heblo.Domain.Features.Users;
using MediatR;

namespace Anela.Heblo.Application.Features.MarketingAds.Proposals.UseCases.GetAdProposal;

public class GetAdProposalHandler : IRequestHandler<GetAdProposalRequest, GetAdProposalResponse>
{
    private readonly IAdProposalRepository _proposals;
    private readonly IAdsReadModel _readModel;
    private readonly IAdLimitsContextBuilder _limitsContext;
    private readonly IAdLimitsEvaluator _evaluator;
    private readonly ICurrentUserService _currentUser;

    public GetAdProposalHandler(IAdProposalRepository proposals, IAdsReadModel readModel, IAdLimitsContextBuilder limitsContext,
        IAdLimitsEvaluator evaluator, ICurrentUserService currentUser)
    {
        _proposals = proposals;
        _readModel = readModel;
        _limitsContext = limitsContext;
        _evaluator = evaluator;
        _currentUser = currentUser;
    }

    public async Task<GetAdProposalResponse> Handle(GetAdProposalRequest request, CancellationToken cancellationToken)
    {
        var proposal = await _proposals.GetReadOnlyAsync(request.Id, cancellationToken);
        if (proposal is null) return new GetAdProposalResponse(ErrorCodes.MarketingAdsProposalNotFound);

        var platform = Enum.Parse<AdPlatform>(proposal.Platform);
        var names = await _readModel.GetEntityNamesAsync(platform, proposal.AccountExternalId, AdProposalDtoMapper.TargetIds(proposal), cancellationToken);

        AdLimitsEvaluation? evaluation = null;
        if (proposal.Status == AdProposalStatus.Pending)
        {
            var actions = AdActionJson.Deserialize(proposal.GetCurrentVersion().ActionsJson);
            var viewer = _currentUser.GetCurrentUser().Id;
            evaluation = _evaluator.Evaluate(await _limitsContext.BuildAsync(proposal, actions, ApprovalChannel.Web, viewer, cancellationToken));
        }

        return new GetAdProposalResponse { Proposal = AdProposalDtoMapper.ToDetail(proposal, names, evaluation) };
    }
}
