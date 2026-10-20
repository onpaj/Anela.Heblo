namespace Anela.Heblo.Domain.Features.MarketingAds;

public interface IAgentRunRepository
{
    void Add(AgentRun run);
    Task<AgentRun?> FindAsync(Guid id, CancellationToken cancellationToken);    // tracked
    Task<(IReadOnlyList<AgentRun> Items, int TotalCount)> ListAsync(int page, int pageSize, CancellationToken cancellationToken);
    Task SaveChangesAsync(CancellationToken cancellationToken);
}
