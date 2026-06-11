namespace Anela.Heblo.Domain.Features.GridLayouts;

public interface IGridLayoutRepository
{
    /// <exception cref="GridLayoutPersistenceException">
    /// Thrown when the underlying persistence layer fails (e.g. PostgreSQL error).
    /// </exception>
    Task<GridLayout?> GetAsync(string userId, string gridKey, CancellationToken cancellationToken = default);

    /// <exception cref="GridLayoutPersistenceException">
    /// Thrown when the underlying persistence layer fails (e.g. PostgreSQL error).
    /// </exception>
    Task UpsertAsync(string userId, string gridKey, string layoutJson, CancellationToken cancellationToken = default);

    /// <exception cref="GridLayoutPersistenceException">
    /// Thrown when the underlying persistence layer fails (e.g. PostgreSQL error).
    /// </exception>
    Task DeleteAsync(string userId, string gridKey, CancellationToken cancellationToken = default);
}
