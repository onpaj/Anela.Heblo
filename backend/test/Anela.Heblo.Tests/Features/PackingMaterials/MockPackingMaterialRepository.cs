using System.Linq.Expressions;
using Anela.Heblo.Domain.Features.PackingMaterials;

namespace Anela.Heblo.Tests.Features.PackingMaterials;

public class MockPackingMaterialRepository : IPackingMaterialRepository
{
    private List<PackingMaterial> _materials = new();
    private readonly Dictionary<DateOnly, bool> _dailyProcessingStatus = new();
    private Exception? _saveChangesException;

    public List<PackingMaterial> UpdatedMaterials { get; } = new();
    public IReadOnlyList<PackingMaterial> Materials => _materials;
    public bool GetAllAsyncWasCalled { get; private set; }

    public void SetMaterials(IEnumerable<PackingMaterial> materials)
    {
        _materials = new List<PackingMaterial>(materials);
    }

    public void SetHasDailyProcessingBeenRun(DateOnly date, bool hasRun)
    {
        _dailyProcessingStatus[date] = hasRun;
    }

    public void SetSaveChangesException(Exception ex)
    {
        _saveChangesException = ex;
    }

    public Task<IEnumerable<PackingMaterial>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        GetAllAsyncWasCalled = true;
        return Task.FromResult<IEnumerable<PackingMaterial>>(_materials);
    }

    public Task<PackingMaterial?> GetByIdAsync(int id, CancellationToken cancellationToken = default)
    {
        var material = _materials.FirstOrDefault(m => m.Id == id);
        return Task.FromResult(material);
    }

    public Task<IEnumerable<PackingMaterialLog>> GetRecentLogsAsync(int packingMaterialId, DateTime fromDate, CancellationToken cancellationToken = default)
    {
        return Task.FromResult<IEnumerable<PackingMaterialLog>>(new List<PackingMaterialLog>());
    }

    public Dictionary<int, List<PackingMaterialLog>> RecentLogsByMaterial { get; } = new();

    public Task<IReadOnlyDictionary<int, IReadOnlyList<PackingMaterialLog>>> GetRecentLogsForMaterialsAsync(
        IEnumerable<int> packingMaterialIds,
        DateTime fromDate,
        CancellationToken cancellationToken = default)
    {
        var ids = packingMaterialIds.ToHashSet();
        var dict = new Dictionary<int, IReadOnlyList<PackingMaterialLog>>();
        foreach (var kvp in RecentLogsByMaterial)
        {
            if (!ids.Contains(kvp.Key)) continue;
            var filtered = kvp.Value
                .Where(l => l.CreatedAt >= fromDate)
                .OrderByDescending(l => l.CreatedAt)
                .ToList();
            if (filtered.Count > 0)
            {
                dict[kvp.Key] = filtered;
            }
        }
        return Task.FromResult<IReadOnlyDictionary<int, IReadOnlyList<PackingMaterialLog>>>(dict);
    }

    public Task<bool> HasDailyProcessingBeenRunAsync(DateOnly date, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(_dailyProcessingStatus.TryGetValue(date, out var hasRun) && hasRun);
    }

    public Task<PackingMaterial> AddAsync(PackingMaterial entity, CancellationToken cancellationToken = default)
    {
        _materials.Add(entity);
        return Task.FromResult(entity);
    }

    public Task<IEnumerable<PackingMaterial>> AddRangeAsync(IEnumerable<PackingMaterial> entities, CancellationToken cancellationToken = default)
    {
        _materials.AddRange(entities);
        return Task.FromResult(entities);
    }

    public Task UpdateAsync(PackingMaterial entity, CancellationToken cancellationToken = default)
    {
        UpdatedMaterials.Add(entity);
        return Task.CompletedTask;
    }

    public Task DeleteAsync(PackingMaterial entity, CancellationToken cancellationToken = default)
    {
        _materials.Remove(entity);
        return Task.CompletedTask;
    }

    public Task DeleteAsync(int id, CancellationToken cancellationToken = default)
    {
        var material = _materials.FirstOrDefault(m => m.Id == id);
        if (material != null)
        {
            _materials.Remove(material);
        }
        return Task.CompletedTask;
    }

    public Task DeleteRangeAsync(IEnumerable<PackingMaterial> entities, CancellationToken cancellationToken = default)
    {
        foreach (var entity in entities)
        {
            _materials.Remove(entity);
        }
        return Task.CompletedTask;
    }

    public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        if (_saveChangesException != null)
            throw _saveChangesException;
        return Task.FromResult(0);
    }

    public Task<IEnumerable<PackingMaterial>> FindAsync(Expression<Func<PackingMaterial, bool>> predicate, CancellationToken cancellationToken = default)
    {
        var compiled = predicate.Compile();
        var results = _materials.Where(compiled);
        return Task.FromResult(results);
    }

    public Task<PackingMaterial?> SingleOrDefaultAsync(Expression<Func<PackingMaterial, bool>> predicate, CancellationToken cancellationToken = default)
    {
        var compiled = predicate.Compile();
        var result = _materials.SingleOrDefault(compiled);
        return Task.FromResult(result);
    }

    public Task<bool> AnyAsync(Expression<Func<PackingMaterial, bool>> predicate, CancellationToken cancellationToken = default)
    {
        var compiled = predicate.Compile();
        var result = _materials.Any(compiled);
        return Task.FromResult(result);
    }

    public Task<int> CountAsync(Expression<Func<PackingMaterial, bool>>? predicate = null, CancellationToken cancellationToken = default)
    {
        var count = predicate == null ? _materials.Count : _materials.Count(predicate.Compile());
        return Task.FromResult(count);
    }

    public List<PackingMaterialConsumption> AddedConsumptionRows { get; } = new();
    public Dictionary<DateOnly, List<PackingMaterialConsumption>> ConsumptionRowsByDate { get; } = new();
    public List<PackingMaterialDailyRun> AddedDailyRuns { get; } = new();

    public Task<IEnumerable<PackingMaterial>> GetAllWithAllocationsAsync(CancellationToken cancellationToken = default)
        => Task.FromResult<IEnumerable<PackingMaterial>>(_materials);

    public Task<PackingMaterial?> GetByIdWithAllocationsAsync(int id, CancellationToken cancellationToken = default)
        => Task.FromResult(_materials.FirstOrDefault(m => m.Id == id));

    public Task AddConsumptionRowsAsync(IEnumerable<PackingMaterialConsumption> rows, CancellationToken cancellationToken = default)
    {
        AddedConsumptionRows.AddRange(rows);
        return Task.CompletedTask;
    }

    public Task<IEnumerable<PackingMaterialConsumption>> GetConsumptionsByDateAsync(DateOnly date, CancellationToken cancellationToken = default)
        => Task.FromResult<IEnumerable<PackingMaterialConsumption>>(ConsumptionRowsByDate.TryGetValue(date, out var rows) ? rows : new List<PackingMaterialConsumption>());

    public Task AddDailyRunAsync(PackingMaterialDailyRun run, CancellationToken cancellationToken = default)
    {
        AddedDailyRuns.Add(run);
        return Task.CompletedTask;
    }
}