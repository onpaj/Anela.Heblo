using Anela.Heblo.Domain.Features.PackingMaterials;
using Anela.Heblo.Domain.Features.PackingMaterials.Enums;
using Anela.Heblo.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Anela.Heblo.Persistence.PackingMaterials;

public class PackingMaterialRepository : BaseRepository<PackingMaterial, int>, IPackingMaterialRepository
{
    public PackingMaterialRepository(ApplicationDbContext context) : base(context)
    {
    }

    public async Task<IEnumerable<PackingMaterialLog>> GetRecentLogsAsync(int packingMaterialId, DateTime fromDate, CancellationToken cancellationToken = default)
    {
        return await Context.Set<PackingMaterialLog>()
            .Where(log => log.PackingMaterialId == packingMaterialId && log.CreatedAt >= fromDate)
            .OrderByDescending(log => log.CreatedAt)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyDictionary<int, IReadOnlyList<PackingMaterialLog>>> GetRecentLogsForMaterialsAsync(
        IEnumerable<int> packingMaterialIds,
        DateTime fromDate,
        CancellationToken cancellationToken = default)
    {
        var ids = packingMaterialIds as IReadOnlyCollection<int> ?? packingMaterialIds.ToArray();
        if (ids.Count == 0)
        {
            return new Dictionary<int, IReadOnlyList<PackingMaterialLog>>();
        }

        var logs = await Context.Set<PackingMaterialLog>()
            .Where(log => ids.Contains(log.PackingMaterialId) && log.CreatedAt >= fromDate)
            .OrderByDescending(log => log.CreatedAt)
            .ToListAsync(cancellationToken);

        return logs
            .GroupBy(log => log.PackingMaterialId)
            .ToDictionary(
                g => g.Key,
                g => (IReadOnlyList<PackingMaterialLog>)g.ToList());
    }

    public async Task<bool> HasDailyProcessingBeenRunAsync(DateOnly date, CancellationToken cancellationToken = default)
    {
        return await Context.Set<PackingMaterialDailyRun>()
            .AnyAsync(r => r.Date == date, cancellationToken);
    }

    public async Task<bool> AddDailyRunAsync(PackingMaterialDailyRun run, CancellationToken cancellationToken = default)
    {
        await Context.Set<PackingMaterialDailyRun>().AddAsync(run, cancellationToken);
        try
        {
            await Context.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateException ex) when (IsDuplicateDailyRunViolation(ex))
        {
            // Detach the entity so the context is not left in a broken state after the failed save.
            Context.Entry(run).State = EntityState.Detached;
            return false;
        }
    }

    public async Task<IEnumerable<PackingMaterial>> GetAllWithAllocationsAsync(CancellationToken cancellationToken = default)
    {
        return await DbSet
            .Include(pm => pm.Allocations)
            .ToListAsync(cancellationToken);
    }

    public async Task<PackingMaterial?> GetByIdWithAllocationsAsync(int id, CancellationToken cancellationToken = default)
    {
        return await DbSet
            .Include(pm => pm.Allocations)
            .FirstOrDefaultAsync(pm => pm.Id == id, cancellationToken);
    }

    public async Task AddConsumptionRowsAsync(IEnumerable<PackingMaterialConsumption> rows, CancellationToken cancellationToken = default)
    {
        await Context.Set<PackingMaterialConsumption>().AddRangeAsync(rows, cancellationToken);
    }

    public async Task<IEnumerable<PackingMaterialConsumption>> GetConsumptionsByDateAsync(DateOnly date, CancellationToken cancellationToken = default)
    {
        return await Context.Set<PackingMaterialConsumption>()
            .Where(c => c.Date == date)
            .ToListAsync(cancellationToken);
    }

    private static bool IsDuplicateDailyRunViolation(DbUpdateException ex) =>
        ex.InnerException is PostgresException pg
        && pg.SqlState == PostgresErrorCodes.UniqueViolation
        && string.Equals(pg.ConstraintName, "IX_PackingMaterialDailyRuns_Date", StringComparison.Ordinal);

    public async Task<(IReadOnlyList<MaterialConsumptionHistoryRecord> Items, int TotalCount)> GetConsumptionHistoryAsync(
        MaterialConsumptionHistoryFilter filter,
        int skip,
        int take,
        bool ascending,
        CancellationToken cancellationToken = default)
    {
        var consumptions = Context.Set<PackingMaterialConsumption>().AsQueryable();
        if (filter.DateFrom.HasValue) consumptions = consumptions.Where(c => c.Date >= filter.DateFrom.Value);
        if (filter.DateTo.HasValue) consumptions = consumptions.Where(c => c.Date <= filter.DateTo.Value);
        if (filter.PackingMaterialId.HasValue) consumptions = consumptions.Where(c => c.PackingMaterialId == filter.PackingMaterialId.Value);
        if (filter.ConsumptionType.HasValue) consumptions = consumptions.Where(c => c.ConsumptionType == filter.ConsumptionType.Value);
        if (!string.IsNullOrWhiteSpace(filter.ProductCode)) consumptions = consumptions.Where(c => c.ProductCode == filter.ProductCode);
        if (!string.IsNullOrWhiteSpace(filter.InvoiceId)) consumptions = consumptions.Where(c => c.InvoiceId == filter.InvoiceId);

        IQueryable<MaterialConsumptionHistoryRecord> combined = consumptions.Select(c => new MaterialConsumptionHistoryRecord
        {
            RecordType = HistoryRecordType.Consumption,
            PackingMaterialId = c.PackingMaterialId,
            Date = c.Date,
            CreatedAt = c.CreatedAt,
            ConsumptionType = c.ConsumptionType,
            InvoiceId = c.InvoiceId,
            ProductCode = c.ProductCode,
            ProductQuantity = c.ProductQuantity,
            Amount = c.Amount,
            OldQuantity = null,
            NewQuantity = null,
            ChangeAmount = null,
            LogType = null,
            UserId = null,
        });

        // Consumption-only filters cannot match quantity-change logs (logs lack those fields),
        // so when any is set, exclude the logs source entirely.
        var consumptionOnlyFilter = filter.ConsumptionType.HasValue
            || !string.IsNullOrWhiteSpace(filter.ProductCode)
            || !string.IsNullOrWhiteSpace(filter.InvoiceId);

        if (!consumptionOnlyFilter)
        {
            var logs = Context.Set<PackingMaterialLog>().AsQueryable();
            if (filter.DateFrom.HasValue) logs = logs.Where(l => l.Date >= filter.DateFrom.Value);
            if (filter.DateTo.HasValue) logs = logs.Where(l => l.Date <= filter.DateTo.Value);
            if (filter.PackingMaterialId.HasValue) logs = logs.Where(l => l.PackingMaterialId == filter.PackingMaterialId.Value);

            var logRecords = logs.Select(l => new MaterialConsumptionHistoryRecord
            {
                RecordType = HistoryRecordType.QuantityChange,
                PackingMaterialId = l.PackingMaterialId,
                Date = l.Date,
                CreatedAt = l.CreatedAt,
                ConsumptionType = null,
                InvoiceId = null,
                ProductCode = null,
                ProductQuantity = null,
                Amount = null,
                OldQuantity = l.OldQuantity,
                NewQuantity = l.NewQuantity,
                ChangeAmount = l.NewQuantity - l.OldQuantity,
                LogType = l.LogType,
                UserId = l.UserId,
            });

            combined = combined.Concat(logRecords);
        }

        var totalCount = await combined.CountAsync(cancellationToken);

        var ordered = ascending
            ? combined.OrderBy(r => r.Date).ThenBy(r => r.CreatedAt)
            : combined.OrderByDescending(r => r.Date).ThenByDescending(r => r.CreatedAt);

        var items = await ordered.Skip(skip).Take(take).ToListAsync(cancellationToken);

        return (items, totalCount);
    }
}