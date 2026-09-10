namespace Anela.Heblo.Application.Features.DataQuality.Contracts;

public interface IDqtEshopStockSource
{
    Task<IReadOnlyList<DqtEshopStockItem>> ListAsync(CancellationToken cancellationToken);
}
