using Anela.Heblo.Domain.Features.Manufacture;
using Rem.FlexiBeeSDK.Client.Clients.Products.StockMovement;
using Rem.FlexiBeeSDK.Model.Products.StockMovement;

namespace Anela.Heblo.Adapters.Flexi.Manufacture;

public class FlexiManufactureHistoryClient : IManufactureHistoryClient
{
    private readonly IStockItemsMovementClient _stockItemsMovementClient;

    private const int ManufactureDocumentTypeId = 56;
    
    public FlexiManufactureHistoryClient(
        IStockItemsMovementClient  stockItemsMovementClient
    )
    {
        _stockItemsMovementClient = stockItemsMovementClient;
    }

    
    public async Task<List<ManufactureHistoryRecord>> GetHistoryAsync(DateTime dateFrom, DateTime dateTo, string? productCode = null,
        CancellationToken cancellationToken = default)
    {
        var movements = await _stockItemsMovementClient.GetAsync(dateFrom, dateTo, StockMovementDirection.In, documentTypeId: ManufactureDocumentTypeId, cancellationToken: cancellationToken);

        var query = movements.AsQueryable();
        
        // Filtrovat podle produktového kódu, pokud je zadán
        if (!string.IsNullOrEmpty(productCode))
        {
            query = query.Where(m => m.ProductCode != null && m.ProductCode.Contains(productCode));
        }

        // Seskupit podle data a produktového kódu a spočítat celkové množství
        var statistics = query
            .Where(m => m.Date != default && !string.IsNullOrEmpty(m.ProductCode))
            .GroupBy(m => new { 
                Date = m.Date.Date, // Pouze datum bez času
                ProductCode = m.ProductCode!.RemoveCodePrefix() 
            })
            .Select(g => new ManufactureHistoryRecord
            {
                Date = g.Key.Date,
                ProductCode = g.Key.ProductCode,
                Amount = g.Sum(m => m.Amount)
            })
            .OrderBy(s => s.Date)
            .ThenBy(s => s.ProductCode)
            .ToList();

        return statistics;
    }
}