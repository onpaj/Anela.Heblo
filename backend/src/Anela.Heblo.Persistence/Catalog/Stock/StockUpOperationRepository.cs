using Anela.Heblo.Domain.Features.Catalog.Stock;
using Anela.Heblo.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Persistence.Catalog.Stock;

public class StockUpOperationRepository : BaseRepository<StockUpOperation, int>, IStockUpOperationRepository
{
    private readonly ILogger<StockUpOperationRepository> _logger;

    public StockUpOperationRepository(ApplicationDbContext context, ILogger<StockUpOperationRepository> logger)
        : base(context)
    {
        _logger = logger;
    }

    public async Task<StockUpOperation?> GetByDocumentNumberAsync(string documentNumber, CancellationToken ct = default)
    {
        return await Context.Set<StockUpOperation>()
            .FirstOrDefaultAsync(x => x.DocumentNumber == documentNumber, ct);
    }

    public async Task<List<StockUpOperation>> GetByStateAsync(StockUpOperationState state, CancellationToken ct = default)
    {
        return await Context.Set<StockUpOperation>()
            .Where(x => x.State == state)
            .OrderByDescending(x => x.CreatedAt)
            .ToListAsync(ct);
    }

    public async Task<List<StockUpOperation>> GetFailedOperationsAsync(CancellationToken ct = default)
    {
        return await GetByStateAsync(StockUpOperationState.Failed, ct);
    }

    public async Task<List<StockUpOperation>> GetBySourceAsync(
        StockUpSourceType sourceType,
        int sourceId,
        CancellationToken ct = default)
    {
        return await Context.Set<StockUpOperation>()
            .Where(op => op.SourceType == sourceType && op.SourceId == sourceId)
            .OrderBy(op => op.CreatedAt)
            .ToListAsync(ct);
    }

    public IQueryable<StockUpOperation> GetAll()
    {
        return Context.Set<StockUpOperation>().AsQueryable();
    }

    public async Task<(List<StockUpOperation> Items, int TotalCount)> QueryAsync(StockUpOperationFilter filter, CancellationToken ct = default)
    {
        var query = Context.Set<StockUpOperation>().AsQueryable();

        // Filter by state if provided (supports "Active" special value)
        if (!string.IsNullOrWhiteSpace(filter.State))
        {
            if (filter.State.Equals("Active", StringComparison.OrdinalIgnoreCase))
            {
                // Active = Pending OR Submitted OR Failed (not Completed)
                query = query.Where(x =>
                    x.State == StockUpOperationState.Pending ||
                    x.State == StockUpOperationState.Submitted ||
                    x.State == StockUpOperationState.Failed);
            }
            else if (Enum.TryParse<StockUpOperationState>(filter.State, true, out var parsedState))
            {
                query = query.Where(x => x.State == parsedState);
            }
        }

        // Filter by SourceType if provided
        if (filter.SourceType.HasValue)
        {
            query = query.Where(x => x.SourceType == filter.SourceType.Value);
        }

        // Filter by SourceId if provided
        if (filter.SourceId.HasValue)
        {
            query = query.Where(x => x.SourceId == filter.SourceId.Value);
        }

        // Filter by ProductCode (exact match) if provided
        if (!string.IsNullOrWhiteSpace(filter.ProductCode))
        {
            query = query.Where(x => x.ProductCode == filter.ProductCode);
        }

        // Filter by DocumentNumber (partial match, case-insensitive) if provided
        if (!string.IsNullOrWhiteSpace(filter.DocumentNumber))
        {
            query = query.Where(x => x.DocumentNumber.ToLower().Contains(filter.DocumentNumber.ToLower()));
        }

        // Filter by date range if provided
        if (filter.CreatedFrom.HasValue)
        {
            query = query.Where(x => x.CreatedAt >= filter.CreatedFrom.Value);
        }

        if (filter.CreatedTo.HasValue)
        {
            // Include the entire day by adding 1 day and using < instead of <=
            var endDate = filter.CreatedTo.Value.Date.AddDays(1);
            query = query.Where(x => x.CreatedAt < endDate);
        }

        // Apply sorting
        var sortBy = filter.SortBy?.ToLower() ?? "createdat";
        query = sortBy switch
        {
            "id" => filter.SortDescending ? query.OrderByDescending(x => x.Id) : query.OrderBy(x => x.Id),
            "documentnumber" => filter.SortDescending ? query.OrderByDescending(x => x.DocumentNumber) : query.OrderBy(x => x.DocumentNumber),
            "productcode" => filter.SortDescending ? query.OrderByDescending(x => x.ProductCode) : query.OrderBy(x => x.ProductCode),
            "state" => filter.SortDescending ? query.OrderByDescending(x => x.State) : query.OrderBy(x => x.State),
            _ => filter.SortDescending ? query.OrderByDescending(x => x.CreatedAt) : query.OrderBy(x => x.CreatedAt),
        };

        // Get total count after filtering but before pagination
        var totalCount = await query.CountAsync(ct);

        // Apply pagination
        var skip = (filter.Page - 1) * filter.PageSize;
        var items = await query
            .Skip(skip)
            .Take(filter.PageSize)
            .ToListAsync(ct);

        return (items, totalCount);
    }

    public async Task<(int Pending, int Submitted, int Failed)> GetActiveCountsAsync(StockUpSourceType? sourceType, CancellationToken ct = default)
    {
        // Single source of truth for the active-state set. The PostgreSQL partial index
        // IX_StockUpOperations_State_Active uses the same integer set in its predicate.
        // Cast through (int) so silent breakage is impossible if enum values are renumbered.
        // ActiveStates.Contains((int)x.State) translates to a literal IN (0, 1, 3) in SQL,
        // which the planner can match to the partial-index predicate.
        var activeStates = new[] { (int)StockUpOperationState.Pending, (int)StockUpOperationState.Submitted, (int)StockUpOperationState.Failed };

        var query = Context.Set<StockUpOperation>()
            .Where(x => activeStates.Contains((int)x.State));

        if (sourceType.HasValue)
        {
            query = query.Where(x => x.SourceType == sourceType.Value);
        }

        var counts = await query
            .GroupBy(x => x.State)
            .Select(g => new { State = g.Key, Count = g.Count() })
            .ToListAsync(ct);

        var pending = counts.FirstOrDefault(x => x.State == StockUpOperationState.Pending)?.Count ?? 0;
        var submitted = counts.FirstOrDefault(x => x.State == StockUpOperationState.Submitted)?.Count ?? 0;
        var failed = counts.FirstOrDefault(x => x.State == StockUpOperationState.Failed)?.Count ?? 0;

        return (pending, submitted, failed);
    }
}
