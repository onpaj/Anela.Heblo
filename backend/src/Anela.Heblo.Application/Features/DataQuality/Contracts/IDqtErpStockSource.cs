namespace Anela.Heblo.Application.Features.DataQuality.Contracts;

public interface IDqtErpStockSource
{
    Task<IReadOnlyList<DqtErpStockItem>> ListAsync(CancellationToken cancellationToken);
}
