using Hangfire;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.MarketingAds.Proposals.Execution;

public class HangfireAdProposalExecutionEnqueuer : IAdProposalExecutionEnqueuer
{
    private readonly IBackgroundJobClient _client;
    private readonly ILogger<HangfireAdProposalExecutionEnqueuer> _logger;

    public HangfireAdProposalExecutionEnqueuer(IBackgroundJobClient client, ILogger<HangfireAdProposalExecutionEnqueuer> logger)
    {
        _client = client;
        _logger = logger;
    }

    public string? EnqueueExecution(int proposalId)
    {
        try
        {
            return _client.Enqueue<AdProposalExecutionJob>(job => job.RunAsync(proposalId, CancellationToken.None));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to enqueue execution of ad proposal {ProposalId}", proposalId);
            return null;
        }
    }
}
