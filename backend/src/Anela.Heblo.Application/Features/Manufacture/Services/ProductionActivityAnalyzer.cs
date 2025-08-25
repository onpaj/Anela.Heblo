using Anela.Heblo.Domain.Features.Manufacture;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.Manufacture.Services;

public class ProductionActivityAnalyzer : IProductionActivityAnalyzer
{
    private readonly ILogger<ProductionActivityAnalyzer> _logger;

    public ProductionActivityAnalyzer(ILogger<ProductionActivityAnalyzer> logger)
    {
        _logger = logger;
    }

    public bool IsInActiveProduction(IEnumerable<ManufactureHistoryRecord> manufactureHistory, int dayThreshold = 30)
    {
        var cutoffDate = DateTime.UtcNow.AddDays(-dayThreshold);
        
        var recentProduction = manufactureHistory.Any(m => 
            m.Date >= cutoffDate && m.Amount > 0);

        _logger.LogDebug("Checking production activity: cutoff date {CutoffDate:yyyy-MM-dd}, active: {IsActive}",
            cutoffDate, recentProduction);

        return recentProduction;
    }

    public DateTime? GetLastProductionDate(IEnumerable<ManufactureHistoryRecord> manufactureHistory)
    {
        var lastProduction = manufactureHistory
            .Where(m => m.Amount > 0)
            .OrderByDescending(m => m.Date)
            .FirstOrDefault();

        var lastDate = lastProduction?.Date;

        _logger.LogDebug("Last production date: {LastDate}",
            lastDate?.ToString("yyyy-MM-dd") ?? "None");

        return lastDate;
    }

    public double CalculateAverageProductionFrequency(IEnumerable<ManufactureHistoryRecord> manufactureHistory, int analysisMonths = 12)
    {
        var analysisStartDate = DateTime.UtcNow.AddMonths(-analysisMonths);
        
        var productionDates = manufactureHistory
            .Where(m => m.Date >= analysisStartDate && m.Amount > 0)
            .Select(m => m.Date)
            .OrderBy(d => d)
            .ToList();

        if (productionDates.Count < 2)
        {
            _logger.LogDebug("Insufficient production data for frequency calculation: {Count} records", productionDates.Count);
            return double.PositiveInfinity; // Not enough data
        }

        var intervals = new List<double>();
        for (int i = 1; i < productionDates.Count; i++)
        {
            var daysBetween = (productionDates[i] - productionDates[i - 1]).TotalDays;
            intervals.Add(daysBetween);
        }

        var averageFrequency = intervals.Average();

        _logger.LogDebug("Calculated average production frequency: {Frequency} days ({Count} intervals analyzed)",
            averageFrequency, intervals.Count);

        return averageFrequency;
    }
}