using Anela.Heblo.Domain.Features.MarketingAds;
using Microsoft.EntityFrameworkCore;

namespace Anela.Heblo.Tests.Features.MarketingAds.Proposals.Execution;

/// <summary>Holds every loader until all participants have loaded the proposal, so their claims race.</summary>
internal sealed class LoadBarrier
{
    private readonly int _participants;
    private readonly TaskCompletionSource _allLoaded = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _loads;

    public LoadBarrier(int participants) => _participants = participants;

    public int Loads => _loads;

    public Task ArriveAsync()
    {
        if (Interlocked.Increment(ref _loads) >= _participants)
        {
            _allLoaded.TrySetResult();
        }
        return _allLoaded.Task.WaitAsync(TimeSpan.FromSeconds(10));
    }
}

/// <summary>Delegates to a real repository; can fail chosen SaveChanges calls (1-based) or hold loads at a barrier.</summary>
internal sealed class FaultyProposalRepository : IAdProposalRepository
{
    private readonly IAdProposalRepository _inner;
    private readonly HashSet<int> _failingSaves;
    private readonly LoadBarrier? _barrier;
    private int _saves;

    public FaultyProposalRepository(IAdProposalRepository inner, int failOnSaves)
        : this(inner, new[] { failOnSaves })
    {
    }

    public FaultyProposalRepository(IAdProposalRepository inner, int[] failOnSaves)
    {
        _inner = inner;
        _failingSaves = failOnSaves.ToHashSet();
    }

    public FaultyProposalRepository(IAdProposalRepository inner, LoadBarrier barrier)
    {
        _inner = inner;
        _failingSaves = new HashSet<int>();
        _barrier = barrier;
    }

    public void Add(AdProposal proposal) => _inner.Add(proposal);

    public async Task<AdProposal?> GetForUpdateAsync(int id, CancellationToken cancellationToken)
    {
        var proposal = await _inner.GetForUpdateAsync(id, cancellationToken);
        if (_barrier is not null)
        {
            await _barrier.ArriveAsync();
        }
        return proposal;
    }

    public Task<AdProposal?> GetReadOnlyAsync(int id, CancellationToken cancellationToken) => _inner.GetReadOnlyAsync(id, cancellationToken);
    public Task<(IReadOnlyList<AdProposal> Items, int TotalCount)> ListAsync(AdProposalQuery query, CancellationToken cancellationToken) => _inner.ListAsync(query, cancellationToken);
    public Task<int> CountSucceededActionsSinceAsync(string platform, string accountExternalId, string actionType, DateTime sinceUtc, CancellationToken cancellationToken) =>
        _inner.CountSucceededActionsSinceAsync(platform, accountExternalId, actionType, sinceUtc, cancellationToken);
    public Task<int> CountProposalsForRunAsync(Guid agentRunId, CancellationToken cancellationToken) => _inner.CountProposalsForRunAsync(agentRunId, cancellationToken);
    public Task<IReadOnlyList<int>> GetPendingExpiredIdsAsync(DateTime nowUtc, CancellationToken cancellationToken) => _inner.GetPendingExpiredIdsAsync(nowUtc, cancellationToken);
    public Task<IReadOnlyList<int>> GetStuckApprovedIdsAsync(DateTime updatedBeforeUtc, CancellationToken cancellationToken) => _inner.GetStuckApprovedIdsAsync(updatedBeforeUtc, cancellationToken);
    public Task<IReadOnlyList<AdProposalActionExecution>> GetExecutionsInWindowAsync(string platform, string accountExternalId, DateTime fromUtc, DateTime toUtc, CancellationToken cancellationToken) =>
        _inner.GetExecutionsInWindowAsync(platform, accountExternalId, fromUtc, toUtc, cancellationToken);
    public Task<IReadOnlyList<AdApprovalOutcomeCount>> GetApprovalOutcomeCountsAsync(CancellationToken cancellationToken) => _inner.GetApprovalOutcomeCountsAsync(cancellationToken);
    public void DiscardChanges() => _inner.DiscardChanges();

    public Task SaveChangesAsync(CancellationToken cancellationToken) =>
        _failingSaves.Contains(Interlocked.Increment(ref _saves))
            ? throw new DbUpdateException("simulated save failure")
            : _inner.SaveChangesAsync(cancellationToken);

    public async Task<bool> TrySaveChangesAsync(CancellationToken cancellationToken)
    {
        if (_failingSaves.Contains(Interlocked.Increment(ref _saves)))
        {
            _inner.DiscardChanges();
            return false;
        }
        return await _inner.TrySaveChangesAsync(cancellationToken);
    }
}
