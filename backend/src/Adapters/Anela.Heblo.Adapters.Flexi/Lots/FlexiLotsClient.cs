using Anela.Heblo.Domain.Features.Catalog.Lots;

namespace Anela.Heblo.Adapters.Flexi.Lots;

public class FlexiLotsClient : ILotsClient
{
    private readonly Rem.FlexiBeeSDK.Client.Clients.Products.StockToDate.ILotsClient _lotsClient;

    public FlexiLotsClient(Rem.FlexiBeeSDK.Client.Clients.Products.StockToDate.ILotsClient lotsClient)
    {
        _lotsClient = lotsClient;
    }

    public async Task<IReadOnlyList<CatalogLot>> GetAsync(string? productCode = null, int limit = 0, int skip = 0, CancellationToken cancellationToken = default)
    {
        var lots = await _lotsClient.GetAsync(productCode, limit, skip, cancellationToken);
        return lots.Select(s => new CatalogLot()
        {
            ProductCode = s.ProductCode,
            Amount = s.Amount,
            Expiration = s.Expiration.HasValue ? DateOnly.FromDateTime(s.Expiration.Value.Date) : null,
            Lot = s.Lot
        }).ToList();
    }
}