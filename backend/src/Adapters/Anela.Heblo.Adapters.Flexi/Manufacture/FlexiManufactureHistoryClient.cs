using Anela.Heblo.Domain.Features.Manufacture;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Rem.FlexiBeeSDK.Client.Clients.Products.StockMovement;
using Rem.FlexiBeeSDK.Model.Products.StockMovement;

namespace Anela.Heblo.Adapters.Flexi.Manufacture;

public class FlexiManufactureHistoryClient : IManufactureHistoryClient
{
    private readonly IStockItemsMovementClient _stockItemsMovementClient;
    private readonly IMemoryCache _cache;
    private readonly ILogger<FlexiManufactureHistoryClient> _logger;

    private const int ManufactureDocumentTypeId = 56;
    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(5);

    public FlexiManufactureHistoryClient(
        IStockItemsMovementClient stockItemsMovementClient,
        IMemoryCache cache,
        ILogger<FlexiManufactureHistoryClient> logger)
    {
        _stockItemsMovementClient = stockItemsMovementClient;
        _cache = cache;
        _logger = logger;
    }


    public async Task<List<ManufactureHistoryRecord>> GetHistoryAsync(DateTime dateFrom, DateTime dateTo, string? productCode = null,
        CancellationToken cancellationToken = default)
    {
        var cacheKey = $"manufacture-history_{dateFrom:yyyyMMdd}_{dateTo:yyyyMMdd}_{productCode ?? "all"}";
        if (_cache.TryGetValue(cacheKey, out List<ManufactureHistoryRecord>? cached))
        {
            _logger.LogDebug("Returning cached manufacture history for {DateFrom} to {DateTo}", dateFrom, dateTo);
            return cached!;
        }

        IReadOnlyList<StockItemMovementFlexiDto> movements;
        try
        {
            movements = await _stockItemsMovementClient.GetAsync(dateFrom, dateTo, StockMovementDirection.In, documentTypeId: ManufactureDocumentTypeId, cancellationToken: cancellationToken);
        }
        catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning(ex,
                "FlexiBee uzivatelsky-dotaz request timed out (internal HttpClient timeout). " +
                "DateFrom: {DateFrom}, DateTo: {DateTo}",
                dateFrom, dateTo);
            throw;
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation(
                "FlexiBee uzivatelsky-dotaz request was canceled by the caller (client abort). " +
                "DateFrom: {DateFrom}, DateTo: {DateTo}",
                dateFrom, dateTo);
            throw;
        }

        var query = movements.AsQueryable();

        // Filtrovat podle produktového kódu, pokud je zadán
        if (!string.IsNullOrEmpty(productCode))
        {
            query = query.Where(m => m.ProductCode != null && m.ProductCode.Contains(productCode));
        }

        // Seskupit podle data a produktového kódu a spočítat celkové množství
        var statistics = query
            .Where(m => m.Date != default && !string.IsNullOrEmpty(m.ProductCode))
            .GroupBy(m => new
            {
                Date = m.Date.Date, // Pouze datum bez času
                ProductCode = m.ProductCode!.RemoveCodePrefix()
            })
            .Select(g => new ManufactureHistoryRecord
            {
                Date = g.Key.Date,
                ProductCode = g.Key.ProductCode,
                PricePerPiece = (decimal)g.Average(a => a.PricePerUnit),
                PriceTotal = (decimal)g.Sum(s => s.TotalSum),
                Amount = g.Sum(m => m.Amount)
            })
            .OrderBy(s => s.Date)
            .ThenBy(s => s.ProductCode)
            .ToList();

        _cache.Set(cacheKey, statistics, new MemoryCacheEntryOptions
        {
            SlidingExpiration = CacheDuration
        });

        return statistics;
    }
}