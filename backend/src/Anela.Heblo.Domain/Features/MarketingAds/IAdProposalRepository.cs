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
    Task<int> CountProposalsForRunAsync(Guid agentRunId, CancellationToken cancellationToken);
    Task<IReadOnlyList<int>> GetPendingExpiredIdsAsync(DateTime nowUtc, CancellationToken cancellationToken);
    Task<IReadOnlyList<int>> GetStuckApprovedIdsAsync(DateTime updatedBeforeUtc, CancellationToken cancellationToken);
    Task<IReadOnlyList<AdProposalActionExecution>> GetExecutionsInWindowAsync(string platform, string accountExternalId, DateTime fromUtc, DateTime toUtc, CancellationToken cancellationToken);
    Task<IReadOnlyList<AdApprovalOutcomeCount>> GetApprovalOutcomeCountsAsync(CancellationToken cancellationToken);
    void DiscardChanges();
    Task SaveChangesAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Saves; on a concurrency conflict or any other failed update it discards every tracked change (so the
    /// shared context is not poisoned) and returns false. Callers map false to MarketingAdsConcurrentChange.
    /// </summary>
    Task<bool> TrySaveChangesAsync(CancellationToken cancellationToken);
}
