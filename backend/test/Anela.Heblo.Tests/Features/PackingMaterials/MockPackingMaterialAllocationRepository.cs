using System.Linq.Expressions;
using Anela.Heblo.Domain.Features.PackingMaterials;

namespace Anela.Heblo.Tests.Features.PackingMaterials;

public class MockPackingMaterialAllocationRepository : IPackingMaterialAllocationRepository
{
    private readonly List<PackingMaterialAllocation> _allocations = new();
    public List<PackingMaterialAllocation> AddedAllocations { get; } = new();
    public List<PackingMaterialAllocation> UpdatedAllocations { get; } = new();
    public List<PackingMaterialAllocation> DeletedAllocations { get; } = new();

    public void SetAllocations(IEnumerable<PackingMaterialAllocation> allocations)
    {
        _allocations.Clear();
        _allocations.AddRange(allocations);
    }

    public Task<PackingMaterialAllocation?> GetByIdAsync(int id, CancellationToken cancellationToken = default)
        => Task.FromResult(_allocations.FirstOrDefault(a => a.Id == id));

    public Task<IEnumerable<PackingMaterialAllocation>> GetAllAsync(CancellationToken cancellationToken = default)
        => Task.FromResult<IEnumerable<PackingMaterialAllocation>>(_allocations);

    public Task<IEnumerable<PackingMaterialAllocation>> FindAsync(Expression<Func<PackingMaterialAllocation, bool>> predicate, CancellationToken cancellationToken = default)
    {
        var compiled = predicate.Compile();
        return Task.FromResult<IEnumerable<PackingMaterialAllocation>>(_allocations.Where(compiled).ToList());
    }

    public Task<PackingMaterialAllocation?> SingleOrDefaultAsync(Expression<Func<PackingMaterialAllocation, bool>> predicate, CancellationToken cancellationToken = default)
    {
        var compiled = predicate.Compile();
        return Task.FromResult(_allocations.SingleOrDefault(compiled));
    }

    public Task<bool> AnyAsync(Expression<Func<PackingMaterialAllocation, bool>> predicate, CancellationToken cancellationToken = default)
        => Task.FromResult(_allocations.Any(predicate.Compile()));

    public Task<int> CountAsync(Expression<Func<PackingMaterialAllocation, bool>>? predicate = null, CancellationToken cancellationToken = default)
    {
        var count = predicate == null ? _allocations.Count : _allocations.Count(predicate.Compile());
        return Task.FromResult(count);
    }

    public Task<PackingMaterialAllocation> AddAsync(PackingMaterialAllocation entity, CancellationToken cancellationToken = default)
    {
        _allocations.Add(entity);
        AddedAllocations.Add(entity);
        return Task.FromResult(entity);
    }

    public Task<IEnumerable<PackingMaterialAllocation>> AddRangeAsync(IEnumerable<PackingMaterialAllocation> entities, CancellationToken cancellationToken = default)
    {
        var list = entities.ToList();
        _allocations.AddRange(list);
        AddedAllocations.AddRange(list);
        return Task.FromResult<IEnumerable<PackingMaterialAllocation>>(list);
    }

    public Task UpdateAsync(PackingMaterialAllocation entity, CancellationToken cancellationToken = default)
    {
        UpdatedAllocations.Add(entity);
        return Task.CompletedTask;
    }

    public Task DeleteAsync(PackingMaterialAllocation entity, CancellationToken cancellationToken = default)
    {
        _allocations.Remove(entity);
        DeletedAllocations.Add(entity);
        return Task.CompletedTask;
    }

    public Task DeleteAsync(int id, CancellationToken cancellationToken = default)
    {
        var entity = _allocations.FirstOrDefault(a => a.Id == id);
        if (entity != null)
        {
            _allocations.Remove(entity);
            DeletedAllocations.Add(entity);
        }
        return Task.CompletedTask;
    }

    public Task DeleteRangeAsync(IEnumerable<PackingMaterialAllocation> entities, CancellationToken cancellationToken = default)
    {
        foreach (var entity in entities)
        {
            _allocations.Remove(entity);
            DeletedAllocations.Add(entity);
        }
        return Task.CompletedTask;
    }

    public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(0);
}
