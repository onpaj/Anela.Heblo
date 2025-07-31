using System.Linq.Expressions;
using Anela.Heblo.Xcc.Domain;

namespace Anela.Heblo.Xcc.Persistance;

/// <summary>
/// Empty repository implementation that returns empty values for all operations.
/// Useful for testing or when a repository is not yet implemented.
/// </summary>
public class EmptyRepository<TEntity, TKey> : IRepository<TEntity, TKey>
    where TEntity : class, IEntity<TKey>
{
    // Query operations
    public Task<TEntity?> GetByIdAsync(TKey id, CancellationToken cancellationToken = default)
    {
        return Task.FromResult<TEntity?>(null);
    }

    public Task<IEnumerable<TEntity>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult<IEnumerable<TEntity>>(Enumerable.Empty<TEntity>());
    }

    public Task<IEnumerable<TEntity>> FindAsync(Expression<Func<TEntity, bool>> predicate, CancellationToken cancellationToken = default)
    {
        return Task.FromResult<IEnumerable<TEntity>>(Enumerable.Empty<TEntity>());
    }

    public Task<TEntity?> SingleOrDefaultAsync(Expression<Func<TEntity, bool>> predicate, CancellationToken cancellationToken = default)
    {
        return Task.FromResult<TEntity?>(null);
    }

    public Task<bool> AnyAsync(Expression<Func<TEntity, bool>> predicate, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(false);
    }

    public Task<int> CountAsync(Expression<Func<TEntity, bool>>? predicate = null, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(0);
    }

    // Command operations
    public Task<TEntity> AddAsync(TEntity entity, CancellationToken cancellationToken = default)
    {
        // Return the same entity as if it was added
        return Task.FromResult(entity);
    }

    public Task<IEnumerable<TEntity>> AddRangeAsync(IEnumerable<TEntity> entities, CancellationToken cancellationToken = default)
    {
        // Return the same entities as if they were added
        return Task.FromResult(entities);
    }

    public Task UpdateAsync(TEntity entity, CancellationToken cancellationToken = default)
    {
        // No-op for empty repository
        return Task.CompletedTask;
    }

    public Task DeleteAsync(TEntity entity, CancellationToken cancellationToken = default)
    {
        // No-op for empty repository
        return Task.CompletedTask;
    }

    public Task DeleteAsync(TKey id, CancellationToken cancellationToken = default)
    {
        // No-op for empty repository
        return Task.CompletedTask;
    }

    public Task DeleteRangeAsync(IEnumerable<TEntity> entities, CancellationToken cancellationToken = default)
    {
        // No-op for empty repository
        return Task.CompletedTask;
    }

    // Unit of Work operations
    public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        // Return 0 as no changes are saved
        return Task.FromResult(0);
    }
}