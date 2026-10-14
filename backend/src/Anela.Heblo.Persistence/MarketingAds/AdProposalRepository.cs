using Anela.Heblo.Domain.Features.MarketingAds;
using Microsoft.EntityFrameworkCore;

namespace Anela.Heblo.Persistence.MarketingAds;

public class AdProposalRepository : IAdProposalRepository
{
    private const int MaxPageSize = 200;
    private readonly ApplicationDbContext _context;

    public AdProposalRepository(ApplicationDbContext context) => _context = context;

    public void Add(AdProposal proposal) => _context.AdProposals.Add(proposal);

    public Task<AdProposal?> GetForUpdateAsync(int id, CancellationToken cancellationToken) =>
        WithChildren(_context.AdProposals).FirstOrDefaultAsync(p => p.Id == id, cancellationToken);

    public Task<AdProposal?> GetReadOnlyAsync(int id, CancellationToken cancellationToken) =>
        WithChildren(_context.AdProposals.AsNoTracking()).FirstOrDefaultAsync(p => p.Id == id, cancellationToken);

    public async Task<(IReadOnlyList<AdProposal> Items, int TotalCount)> ListAsync(AdProposalQuery query, CancellationToken cancellationToken)
    {
        var proposals = _context.AdProposals.AsNoTracking();
        if (query.Status is { } status) proposals = proposals.Where(p => p.Status == status);
        if (query.Platform is { } platform) proposals = proposals.Where(p => p.Platform == platform);
        if (query.CreatedByPrincipal is { } creator) proposals = proposals.Where(p => p.CreatedByPrincipal == creator);

        var total = await proposals.CountAsync(cancellationToken);
        var page = Math.Max(1, query.Page);
        var size = Math.Clamp(query.PageSize, 1, MaxPageSize);
        var items = await proposals
            .OrderByDescending(p => p.CreatedAt).ThenByDescending(p => p.Id)
            .Skip((page - 1) * size).Take(size)
            .Include(p => p.Versions)
            .AsSplitQuery()
            .ToListAsync(cancellationToken);
        return (items, total);
    }

    public Task<int> CountSucceededActionsSinceAsync(string platform, string accountExternalId, string actionType, DateTime sinceUtc, CancellationToken cancellationToken) =>
        _context.AdProposalActionExecutions.AsNoTracking().CountAsync(e =>
            e.Platform == platform && e.AccountExternalId == accountExternalId && e.ActionType == actionType
            && e.State == AdActionExecutionState.Succeeded && e.ExecutedAt >= sinceUtc, cancellationToken);

    public Task<int> CountProposalsForRunAsync(Guid agentRunId, CancellationToken cancellationToken) =>
        _context.AdProposals.AsNoTracking().CountAsync(p => p.AgentRunId == agentRunId, cancellationToken);

    public async Task<IReadOnlyList<int>> GetPendingExpiredIdsAsync(DateTime nowUtc, CancellationToken cancellationToken) =>
        await _context.AdProposals.AsNoTracking()
            .Where(p => p.Status == AdProposalStatus.Pending && p.ExpiresAt <= nowUtc)
            .Select(p => p.Id).ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<int>> GetStuckApprovedIdsAsync(DateTime updatedBeforeUtc, CancellationToken cancellationToken) =>
        await _context.AdProposals.AsNoTracking()
            .Where(p => p.Status == AdProposalStatus.Approved && p.UpdatedAt <= updatedBeforeUtc)
            .Select(p => p.Id).ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<AdProposalActionExecution>> GetExecutionsInWindowAsync(
        string platform, string accountExternalId, DateTime fromUtc, DateTime toUtc, CancellationToken cancellationToken) =>
        await _context.AdProposalActionExecutions.AsNoTracking()
            .Where(e => e.Platform == platform && e.AccountExternalId == accountExternalId
                && ((e.State == AdActionExecutionState.Succeeded && e.ExecutedAt >= fromUtc && e.ExecutedAt <= toUtc)
                    || (e.RevertState == AdActionRevertState.Reverted && e.RevertedAt >= fromUtc && e.RevertedAt <= toUtc)))
            .OrderBy(e => e.ExecutedAt)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<AdApprovalOutcomeCount>> GetApprovalOutcomeCountsAsync(CancellationToken cancellationToken)
    {
        var rows = await _context.AdProposals.AsNoTracking()
            .Where(p => p.Status != AdProposalStatus.Pending && p.Status != AdProposalStatus.Expired)
            .GroupBy(p => new { p.Platform, p.ActionType })
            .Select(g => new
            {
                g.Key.Platform,
                g.Key.ActionType,
                Decided = g.Count(),
                WithoutEdit = g.Count(p => p.Status != AdProposalStatus.Rejected && p.CurrentVersion == 1),
            })
            .ToListAsync(cancellationToken);
        return rows.Select(r => new AdApprovalOutcomeCount(r.Platform, r.ActionType, r.Decided, r.WithoutEdit)).ToList();
    }

    /// <summary>Drops every tracked entity. Call after a failed SaveChanges so the shared context is not poisoned.</summary>
    public void DiscardChanges() => _context.ChangeTracker.Clear();

    public Task SaveChangesAsync(CancellationToken cancellationToken) => _context.SaveChangesAsync(cancellationToken);

    private static IQueryable<AdProposal> WithChildren(IQueryable<AdProposal> query) => query
        .Include(p => p.Versions)
        .Include(p => p.Approvals)
        .Include(p => p.Executions)
        .AsSplitQuery();
}
