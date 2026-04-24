using Anela.Heblo.Domain.Features.DataQuality;
using Anela.Heblo.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Anela.Heblo.Persistence.DataQuality;

public class DqtRunRepository : BaseRepository<DqtRun, Guid>, IDqtRunRepository
{
    public DqtRunRepository(ApplicationDbContext context) : base(context)
    {
    }

    public async Task<DqtRun?> GetLatestByTestTypeAsync(DqtTestType testType, CancellationToken cancellationToken = default)
    {
        return await DbSet
            .Where(r => r.TestType == testType)
            .OrderByDescending(r => r.StartedAt)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<(List<DqtRun> Items, int TotalCount)> GetPaginatedAsync(
        DqtTestType? testType,
        DqtRunStatus? status,
        int pageNumber,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        var query = DbSet.AsQueryable();

        if (testType.HasValue)
        {
            query = query.Where(r => r.TestType == testType.Value);
        }

        if (status.HasValue)
        {
            query = query.Where(r => r.Status == status.Value);
        }

        var totalCount = await query.CountAsync(cancellationToken);

        var items = await query
            .OrderByDescending(r => r.StartedAt)
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return (items, totalCount);
    }

    public async Task AddResultsAsync(IEnumerable<InvoiceDqtResult> results, CancellationToken cancellationToken = default)
    {
        await Context.Set<InvoiceDqtResult>().AddRangeAsync(results, cancellationToken);
    }

    public async Task<DqtRun?> GetWithResultsAsync(Guid id, int resultPage, int resultPageSize, CancellationToken cancellationToken = default)
    {
        var run = await DbSet
            .FirstOrDefaultAsync(r => r.Id == id, cancellationToken);

        if (run == null)
        {
            return null;
        }

        var results = await Context.Set<InvoiceDqtResult>()
            .Where(r => r.DqtRunId == id)
            .OrderBy(r => r.InvoiceCode)
            .Skip((resultPage - 1) * resultPageSize)
            .Take(resultPageSize)
            .ToListAsync(cancellationToken);

        run.Results.Clear();
        run.Results.AddRange(results);

        return run;
    }
}
