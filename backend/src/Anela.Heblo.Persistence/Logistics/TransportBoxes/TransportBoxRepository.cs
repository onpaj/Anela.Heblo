using Anela.Heblo.Domain.Features.Logistics.Transport;
using Anela.Heblo.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Persistence.Logistics.TransportBoxes;

public class TransportBoxRepository : BaseRepository<TransportBox, int>, ITransportBoxRepository
{
    private readonly ILogger<TransportBoxRepository> _logger;

    public TransportBoxRepository(ApplicationDbContext context, ILogger<TransportBoxRepository> logger)
        : base(context)
    {
        _logger = logger;
    }

    public async Task<(IList<TransportBox> items, int totalCount)> GetPagedListAsync(
        int skip,
        int take,
        string? code = null,
        TransportBoxState? state = null,
        string? productCode = null,
        string? sortBy = null,
        bool sortDescending = false,
        bool isActiveFilter = false)
    {
        var query = DbSet.AsQueryable();

        // Apply filters
        if (!string.IsNullOrWhiteSpace(code))
        {
            query = query.Where(x => x.Code != null && x.Code.ToUpper().Contains(code.ToUpper()));
        }

        if (isActiveFilter)
        {
            // Active filter: all states except Closed
            query = query.Where(x => x.State != TransportBoxState.Closed);
        }
        else if (state.HasValue)
        {
            query = query.Where(x => x.State == state.Value);
        }


        if (!string.IsNullOrWhiteSpace(productCode))
        {
            query = query.Where(x => x.Items.Any(item => item.ProductCode != null && item.ProductCode.ToUpper().Contains(productCode.ToUpper())));
        }

        // Get total count before pagination
        var totalCount = await query.CountAsync();

        // Apply sorting
        query = sortBy?.ToLowerInvariant() switch
        {
            "code" => sortDescending ? query.OrderByDescending(x => x.Code) : query.OrderBy(x => x.Code),
            "state" => sortDescending ? query.OrderByDescending(x => x.State) : query.OrderBy(x => x.State),
            "laststatechanged" => sortDescending ? query.OrderByDescending(x => x.LastStateChanged) : query.OrderBy(x => x.LastStateChanged),
            "location" => sortDescending ? query.OrderByDescending(x => x.Location) : query.OrderBy(x => x.Location),
            _ => sortDescending ? query.OrderByDescending(x => x.Id) : query.OrderBy(x => x.Id)
        };

        // Apply pagination and include related data
        var items = await query
            .Include(x => x.Items)
            .Include(x => x.StateLog)
            .Skip(skip)
            .Take(take)
            .ToListAsync();

        _logger.LogDebug("Retrieved {ItemCount} transport boxes out of {TotalCount} with filters - Code: {Code}, State: {State}, ProductCode: {ProductCode}",
            items.Count, totalCount, code, state, productCode);

        return (items, totalCount);
    }

    public async Task<TransportBox?> GetByIdWithDetailsAsync(int id)
    {
        var transportBox = await DbSet
            .Include(x => x.Items)
            .Include(x => x.StateLog)
            .FirstOrDefaultAsync(x => x.Id == id);

        _logger.LogDebug("Retrieved transport box {Id} with details: {Found}",
            id, transportBox != null);

        return transportBox;
    }

    public async Task<bool> IsBoxCodeActiveAsync(string boxCode)
    {
        var activeStates = new[]
        {
            TransportBoxState.New,
            TransportBoxState.Opened,
            TransportBoxState.InTransit,
            TransportBoxState.Received,
            TransportBoxState.Reserve,
        };

        var upperBoxCode = boxCode.ToUpper();
        var exists = await DbSet
            .Where(x => x.Code == upperBoxCode && activeStates.Contains(x.State))
            .AnyAsync();

        _logger.LogDebug("Checked if box code {BoxCode} is active: {IsActive}", boxCode, exists);

        return exists;
    }

    public async Task<TransportBox?> GetByCodeAsync(string boxCode)
    {
        var upperBoxCode = boxCode.ToUpper();
        var transportBox = await DbSet
            .Include(x => x.Items)
            .Include(x => x.StateLog)
            .FirstOrDefaultAsync(x => x.Code == upperBoxCode);

        _logger.LogDebug("Retrieved transport box by code {BoxCode}: {Found}",
            boxCode, transportBox != null);

        return transportBox;
    }

    public async Task<IEnumerable<TransportBox>> FindAsync(
        System.Linq.Expressions.Expression<Func<TransportBox, bool>> predicate,
        bool includeDetails = false,
        CancellationToken cancellationToken = default)
    {
        var query = DbSet.AsQueryable();

        if (includeDetails)
        {
            query = query
                .Include(x => x.Items)
                .Include(x => x.StateLog);
        }

        var result = await query.Where(predicate).ToListAsync(cancellationToken);

        _logger.LogDebug("Found {Count} transport boxes with predicate, includeDetails: {IncludeDetails}",
            result.Count, includeDetails);

        return result;
    }
}