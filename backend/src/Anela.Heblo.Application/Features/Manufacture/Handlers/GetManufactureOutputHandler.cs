using Anela.Heblo.Application.Features.Manufacture.Model;
using Anela.Heblo.Domain.Features.Catalog;
using Anela.Heblo.Domain.Features.Manufacture;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.Manufacture.Handlers;

public class GetManufactureOutputHandler : IRequestHandler<GetManufactureOutputRequest, GetManufactureOutputResponse>
{
    private readonly IManufactureHistoryClient _manufactureHistoryClient;
    private readonly ICatalogRepository _catalogRepository;
    private readonly ILogger<GetManufactureOutputHandler> _logger;

    public GetManufactureOutputHandler(
        IManufactureHistoryClient manufactureHistoryClient,
        ICatalogRepository catalogRepository,
        ILogger<GetManufactureOutputHandler> logger)
    {
        _manufactureHistoryClient = manufactureHistoryClient;
        _catalogRepository = catalogRepository;
        _logger = logger;
    }

    public async Task<GetManufactureOutputResponse> Handle(
        GetManufactureOutputRequest request,
        CancellationToken cancellationToken)
    {
        // Calculate date range - last N months
        var toDate = DateTime.Now;
        var fromDate = toDate.AddMonths(-request.MonthsBack);
        
        _logger.LogInformation($"Fetching manufacture output from {fromDate:yyyy-MM-dd} to {toDate:yyyy-MM-dd}");

        // Get manufacture history for all products in the period
        var history = await _manufactureHistoryClient.GetHistoryAsync(fromDate, toDate, null, cancellationToken);
        
        // Get catalog items to get product names and difficulty
        var catalogItems = await _catalogRepository.GetAllAsync(cancellationToken);
        var catalogDict = catalogItems
            .Where(w => w.ManufactureDifficulty.HasValue)
            .ToDictionary(c => c.ProductCode, c => c);

        // Group by month and calculate weighted output
        var monthlyData = history
            .GroupBy(h => new { Year = h.Date.Year, Month = h.Date.Month })
            .Select(monthGroup =>
            {
                var monthStr = $"{monthGroup.Key.Year:0000}-{monthGroup.Key.Month:00}";
                
                // Group by product within the month
                var productContributions = monthGroup
                    .GroupBy(h => h.ProductCode)
                    .Select(productGroup =>
                    {
                        var productCode = productGroup.Key;
                        var quantity = productGroup.Sum(p => p.Amount);
                        
                        // Get product info from catalog
                        double difficulty = 1.0; // Default difficulty if not found
                        string productName = productCode;
                        
                        if (catalogDict.TryGetValue(productCode, out var catalogItem))
                        {
                            difficulty = catalogItem.ManufactureDifficulty!.Value;
                            productName = catalogItem.ProductName ?? productCode;
                        }
                        
                        var weightedValue = quantity * difficulty;
                        
                        return new ProductContribution
                        {
                            ProductCode = productCode,
                            ProductName = productName,
                            Quantity = quantity,
                            Difficulty = difficulty,
                            WeightedValue = weightedValue
                        };
                    })
                    .Where(p => p.Quantity > 0) // Filter out zero quantities
                    .OrderByDescending(p => p.WeightedValue)
                    .ToList();
                
                var totalOutput = productContributions.Sum(p => p.WeightedValue);
                
                // Prepare detailed production records for the modal
                var productionDetails = monthGroup
                    .Select(record =>
                    {
                        var productName = catalogDict.ContainsKey(record.ProductCode) 
                            ? catalogDict[record.ProductCode].ProductName ?? record.ProductCode
                            : record.ProductCode;
                            
                        return new ProductionDetail
                        {
                            ProductCode = record.ProductCode,
                            ProductName = productName,
                            Date = record.Date,
                            Amount = record.Amount,
                            PricePerPiece = record.PricePerPiece,
                            PriceTotal = record.PriceTotal,
                            DocumentNumber = record.DocumentNumber
                        };
                    })
                    .OrderBy(p => p.Date)
                    .ToList();
                
                return new ManufactureOutputMonth
                {
                    Month = monthStr,
                    TotalOutput = totalOutput,
                    Products = productContributions,
                    ProductionDetails = productionDetails
                };
            })
            .OrderBy(m => m.Month)
            .ToList();

        // Fill in missing months with zero output
        var result = new List<ManufactureOutputMonth>();
        var currentDate = new DateTime(fromDate.Year, fromDate.Month, 1);
        var endDate = new DateTime(toDate.Year, toDate.Month, 1);
        
        while (currentDate <= endDate)
        {
            var monthStr = $"{currentDate:yyyy-MM}";
            var existingMonth = monthlyData.FirstOrDefault(m => m.Month == monthStr);
            
            if (existingMonth != null)
            {
                result.Add(existingMonth);
            }
            else
            {
                result.Add(new ManufactureOutputMonth
                {
                    Month = monthStr,
                    TotalOutput = 0,
                    Products = new List<ProductContribution>(),
                    ProductionDetails = new List<ProductionDetail>()
                });
            }
            
            currentDate = currentDate.AddMonths(1);
        }

        _logger.LogInformation($"Returning manufacture output for {result.Count} months");

        return new GetManufactureOutputResponse
        {
            Months = result
        };
    }
}