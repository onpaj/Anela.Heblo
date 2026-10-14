using Anela.Heblo.Application.Features.MarketingAds.Contracts;
using Anela.Heblo.Application.Features.MarketingAds.Proposals.Actions;
using Anela.Heblo.Application.Features.MarketingAds.Proposals.Execution;
using Anela.Heblo.Application.Features.MarketingAds.Proposals.Limits;
using Anela.Heblo.Domain.Features.MarketingAds;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.MarketingAds.Proposals.UseCases;

/// <summary>The common tail of Submit and Revise, run only after the change has been saved.</summary>
internal static class AdProposalMutationCompletion
{
    private static readonly IReadOnlyDictionary<string, string> NoNames = new Dictionary<string, string>();

    public static TResponse Complete<TResponse>(AdProposal proposal, IReadOnlyList<AdAction> actions, AdLimitsEvaluation evaluation,
        bool autoApproved, IAdProposalExecutionEnqueuer enqueuer, ILogger logger)
        where TResponse : AdProposalMutationResponse, new()
    {
        if (autoApproved && enqueuer.EnqueueExecution(proposal.Id) is null)
        {
            logger.LogWarning("Ad proposal {ProposalId} was auto-approved but enqueueing failed; the expiry job re-enqueues it.", proposal.Id);
        }

        return new TResponse
        {
            ProposalId = proposal.Id,
            Version = proposal.CurrentVersion,
            Status = proposal.Status,
            AutoApproved = autoApproved,
            Evaluation = AdProposalDtoMapper.ToDto(evaluation),
            DiffLines = AdProposalDiffRenderer.Render(actions, NoNames).ToList(),
        };
    }
}
