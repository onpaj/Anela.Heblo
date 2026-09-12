using Anela.Heblo.Xcc.Domain;

namespace Anela.Heblo.Xcc.Persistance;

/// <summary>
/// Generic repository interface for common database operations
/// </summary>
/// <typeparam name="TEntity">The entity type</typeparam>
/// <typeparam name="TKey">Unique entity key</typeparam>
public interface IRepository<TEntity, TKey> : IReadOnlyRepository<TEntity, TKey>
    where TEntity : class, IEntity<TKey>
{
    // Command operations
    Task<TEntity> AddAsync(TEntity entity, CancellationToken cancellationToken = default);
    Task<IEnumerable<TEntity>> AddRangeAsync(IEnumerable<TEntity> entities, CancellationToken cancellationToken = default);
    Task UpdateAsync(TEntity entity, CancellationToken cancellationToken = default);
    Task DeleteAsync(TEntity entity, CancellationToken cancellationToken = default);
    Task DeleteAsync(TKey id, CancellationToken cancellationToken = default);
    Task DeleteRangeAsync(IEnumerable<TEntity> entities, CancellationToken cancellationToken = default);

    // Unit of Work operations
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Runs <paramref name="operation"/> inside a single all-or-nothing database transaction and
    /// returns its result on success; on any exception the transaction is rolled back and the
    /// original exception is rethrown unchanged. This wraps the entire underlying DbContext, not
    /// just <typeparamref name="TEntity"/> — calling it on one repository also covers writes made
    /// through any other repository sharing the same DbContext instance in the current DI scope
    /// (the same whole-context semantics <see cref="SaveChangesAsync"/> already has).
    /// </summary>
    Task<TResult> ExecuteInTransactionAsync<TResult>(
        Func<CancellationToken, Task<TResult>> operation,
        CancellationToken cancellationToken = default);
}