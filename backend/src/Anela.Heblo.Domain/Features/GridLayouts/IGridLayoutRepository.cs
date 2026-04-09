namespace Anela.Heblo.Domain.Features.GridLayouts;

public interface IGridLayoutRepository
{
    Task<GridLayout?> GetAsync(string userId, string gridKey, CancellationToken cancellationToken = default);
    Task UpsertAsync(string userId, string gridKey, string layoutJson, CancellationToken cancellationToken = default);
    Task DeleteAsync(string userId, string gridKey, CancellationToken cancellationToken = default);
}
