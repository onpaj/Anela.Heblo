using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Anela.Heblo.Domain.Features.Logistics.Transport;
using Anela.Heblo.Persistence.Repository;

namespace Anela.Heblo.Persistence.Repositories;

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
        DateTime? fromDate = null,
        DateTime? toDate = null,
        string? sortBy = null,
        bool sortDescending = false,
        bool isActiveFilter = false)
    {
        var query = DbSet.AsQueryable();

        // Apply filters
        if (!string.IsNullOrWhiteSpace(code))
        {
            query = query.Where(x => x.Code != null && x.Code.Contains(code));
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

        if (fromDate.HasValue)
        {
            query = query.Where(x => x.LastStateChanged >= fromDate.Value);
        }

        if (toDate.HasValue)
        {
            query = query.Where(x => x.LastStateChanged <= toDate.Value);
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

        _logger.LogDebug("Retrieved {ItemCount} transport boxes out of {TotalCount} with filters - Code: {Code}, State: {State}",
            items.Count, totalCount, code, state);

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
}