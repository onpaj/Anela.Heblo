namespace Anela.Heblo.Application.Features.MarketingAds.Proposals.Execution;

public interface IAdProposalExecutionEnqueuer
{
    /// <summary>Returns the Hangfire job id, or null when enqueueing failed (the expiry job re-enqueues stuck Approved proposals).</summary>
    string? EnqueueExecution(int proposalId);
}
