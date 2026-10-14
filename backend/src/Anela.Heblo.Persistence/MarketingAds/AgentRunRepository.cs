using Anela.Heblo.Domain.Features.MarketingAds;
using Microsoft.EntityFrameworkCore;

namespace Anela.Heblo.Persistence.MarketingAds;

public class AgentRunRepository : IAgentRunRepository
{
    private const int MaxPageSize = 200;
    private readonly ApplicationDbContext _context;

    public AgentRunRepository(ApplicationDbContext context) => _context = context;

    public void Add(AgentRun run) => _context.AgentRuns.Add(run);

    public Task<AgentRun?> FindAsync(Guid id, CancellationToken cancellationToken) =>
        _context.AgentRuns.FirstOrDefaultAsync(r => r.Id == id, cancellationToken);

    public async Task<(IReadOnlyList<AgentRun> Items, int TotalCount)> ListAsync(int page, int pageSize, CancellationToken cancellationToken)
    {
        var runs = _context.AgentRuns.AsNoTracking();
        var total = await runs.CountAsync(cancellationToken);
        var size = Math.Clamp(pageSize, 1, MaxPageSize);
        var items = await runs.OrderByDescending(r => r.StartedAt)
            .Skip((Math.Max(1, page) - 1) * size).Take(size).ToListAsync(cancellationToken);
        return (items, total);
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken) => _context.SaveChangesAsync(cancellationToken);
}
