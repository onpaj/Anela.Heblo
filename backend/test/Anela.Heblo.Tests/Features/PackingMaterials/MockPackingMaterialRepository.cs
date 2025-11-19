using System.Linq.Expressions;
using Anela.Heblo.Domain.Features.PackingMaterials;
using Anela.Heblo.Domain.Features.PackingMaterials.Enums;

namespace Anela.Heblo.Tests.Features.PackingMaterials;

public class MockPackingMaterialRepository : IPackingMaterialRepository
{
    private List<PackingMaterial> _materials = new();
    private readonly Dictionary<DateOnly, bool> _dailyProcessingStatus = new();
    public List<PackingMaterial> UpdatedMaterials { get; } = new();
    public bool GetAllAsyncWasCalled { get; private set; }

    public void SetMaterials(IEnumerable<PackingMaterial> materials)
    {
        _materials = new List<PackingMaterial>(materials);
    }

    public void SetHasDailyProcessingBeenRun(DateOnly date, bool hasRun)
    {
        _dailyProcessingStatus[date] = hasRun;
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

    public Task<IEnumerable<PackingMaterial>> GetAllWithLogsAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult<IEnumerable<PackingMaterial>>(_materials);
    }

    public Task<PackingMaterial?> GetByIdWithLogsAsync(int id, CancellationToken cancellationToken = default)
    {
        var material = _materials.FirstOrDefault(m => m.Id == id);
        return Task.FromResult(material);
    }

    public Task<IEnumerable<PackingMaterialLog>> GetRecentLogsAsync(int packingMaterialId, DateTime fromDate, CancellationToken cancellationToken = default)
    {
        return Task.FromResult<IEnumerable<PackingMaterialLog>>(new List<PackingMaterialLog>());
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
}