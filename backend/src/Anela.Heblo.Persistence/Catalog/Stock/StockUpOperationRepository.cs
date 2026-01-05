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

    public IQueryable<StockUpOperation> GetAll()
    {
        return Context.Set<StockUpOperation>().AsQueryable();
    }
}
