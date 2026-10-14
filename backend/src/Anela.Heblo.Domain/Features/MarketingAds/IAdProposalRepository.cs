namespace Anela.Heblo.Domain.Features.MarketingAds;

public sealed class AdProposalQuery
{
    public AdProposalStatus? Status { get; init; }
    public string? Platform { get; init; }
    public string? CreatedByPrincipal { get; init; }
    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 50;
}

public sealed record AdApprovalOutcomeCount(string Platform, string ActionType, int Decided, int ApprovedWithoutEdit);

public interface IAdProposalRepository
{
    void Add(AdProposal proposal);
    Task<AdProposal?> GetForUpdateAsync(int id, CancellationToken cancellationToken);
    Task<AdProposal?> GetReadOnlyAsync(int id, CancellationToken cancellationToken);
    Task<(IReadOnlyList<AdProposal> Items, int TotalCount)> ListAsync(AdProposalQuery query, CancellationToken cancellationToken);
    Task<int> CountSucceededActionsSinceAsync(string platform, string accountExternalId, string actionType, DateTime sinceUtc, CancellationToken cancellationToken);
    /// <summary>
    /// Actions of the current version of OTHER proposals (id != <paramref name="excludeProposalId"/>) for the same platform,
    /// account and action type, in one of <paramref name="statuses"/>, that have no execution row yet. Recorded ones are
    /// either counted by <see cref="CountSucceededActionsSinceAsync"/> or changed nothing.
    /// </summary>
    Task<int> CountUnrecordedActionsOfOtherProposalsAsync(string platform, string accountExternalId, string actionType, int excludeProposalId,
        IReadOnlyCollection<AdProposalStatus> statuses, CancellationToken cancellationToken);
    Task<int> CountProposalsForRunAsync(Guid agentRunId, CancellationToken cancellationToken);
    Task<IReadOnlyList<int>> GetPendingExpiredIdsAsync(DateTime nowUtc, CancellationToken cancellationToken);
    Task<IReadOnlyList<int>> GetStuckApprovedIdsAsync(DateTime updatedBeforeUtc, CancellationToken cancellationToken);
    Task<IReadOnlyList<AdProposalActionExecution>> GetExecutionsInWindowAsync(string platform, string accountExternalId, DateTime fromUtc, DateTime toUtc, CancellationToken cancellationToken);
    Task<IReadOnlyList<AdApprovalOutcomeCount>> GetApprovalOutcomeCountsAsync(CancellationToken cancellationToken);
    void DiscardChanges();
    Task SaveChangesAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Saves; on a lost race (concurrency token or unique key) it discards every tracked change (so the shared
    /// context is not poisoned) and returns false — callers map false to MarketingAdsConcurrentChange. Any other
    /// failed update also discards the tracked changes, then rethrows.
    /// </summary>
    Task<bool> TrySaveChangesAsync(CancellationToken cancellationToken);

    /// <summary>Like <see cref="TrySaveChangesAsync"/>, but says whether a failure was a lost race or another error.</summary>
    Task<AdSaveOutcome> TrySaveChangesWithOutcomeAsync(CancellationToken cancellationToken);
}
