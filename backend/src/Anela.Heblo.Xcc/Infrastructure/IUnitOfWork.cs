using Anela.Heblo.Xcc.Persistance;
using Anela.Heblo.Xcc.Domain;

namespace Anela.Heblo.Xcc.Infrastructure;

/// <summary>
/// Unit of Work interface for coordinating work across multiple repositories
/// and ensuring transactional consistency. Changes are automatically saved on dispose
/// unless explicitly aborted.
/// </summary>
public interface IUnitOfWork : IDisposable, IAsyncDisposable
{
    /// <summary>
    /// Get repository for a specific entity type
    /// </summary>
    /// <typeparam name="TEntity">Entity type</typeparam>
    /// <typeparam name="TKey">Entity key type</typeparam>
    /// <returns>Repository instance</returns>
    IRepository<TEntity, TKey> Repository<TEntity, TKey>()
        where TEntity : class, IEntity<TKey>;

    /// <summary>
    /// Save all changes across all repositories in a single transaction
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Number of affected records</returns>
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Begin a new transaction
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    Task BeginTransactionAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Commit the current transaction
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    Task CommitTransactionAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Rollback the current transaction
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    Task RollbackTransactionAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Abort the unit of work, preventing automatic save on dispose.
    /// Use this when you want to rollback changes instead of committing them.
    /// </summary>
    void Abort();
}