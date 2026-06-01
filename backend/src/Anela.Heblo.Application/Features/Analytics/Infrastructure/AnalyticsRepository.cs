using Anela.Heblo.Application.Features.Analytics.Contracts;
using Anela.Heblo.Application.Features.Analytics.UseCases.GetInvoiceImportStatistics;
using Anela.Heblo.Application.Features.Analytics.UseCases.GetBankStatementImportStatistics;
using Anela.Heblo.Domain.Features.Analytics;
using Anela.Heblo.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Anela.Heblo.Application.Features.Analytics.Infrastructure;

/// <summary>
/// 🔒 PERFORMANCE FIX: Analytics repository with streaming capabilities
/// Prevents memory overload by delegating to IAnalyticsProductSource
/// </summary>
public class AnalyticsRepository : IAnalyticsRepository
{
    private readonly IAnalyticsProductSource _productSource;
    private readonly ApplicationDbContext _dbContext;

    public AnalyticsRepository(IAnalyticsProductSource productSource, ApplicationDbContext dbContext)
    {
        _productSource = productSource;
        _dbContext = dbContext;
    }

    /// <summary>
    /// Streams products with sales to avoid memory overload
    /// Delegates to the product source implementation
    /// </summary>
    public IAsyncEnumerable<AnalyticsProduct> StreamProductsWithSalesAsync(
        DateTime fromDate,
        DateTime toDate,
        AnalyticsProductType[] productTypes,
        CancellationToken cancellationToken = default)
    {
        return _productSource.StreamProductsWithSalesAsync(fromDate, toDate, productTypes, cancellationToken);
    }

    public Task<AnalyticsProduct?> GetProductAnalysisDataAsync(
        string productId,
        DateTime fromDate,
        DateTime toDate,
        CancellationToken cancellationToken = default)
    {
        return _productSource.GetProductAnalysisDataAsync(productId, fromDate, toDate, cancellationToken);
    }

    /// <summary>
    /// Gets daily invoice import statistics for monitoring purposes
    /// </summary>
    public async Task<List<DailyInvoiceCount>> GetInvoiceImportStatisticsAsync(
        DateTime startDate,
        DateTime endDate,
        ImportDateType dateType,
        CancellationToken cancellationToken = default)
    {
        // PostgreSQL timestamp without time zone: work with UTC dates but store as Unspecified
        // Convert input dates to UTC if needed, then to Unspecified for PostgreSQL compatibility
        if (startDate.Kind != DateTimeKind.Utc)
            startDate = startDate.ToUniversalTime();
        if (endDate.Kind != DateTimeKind.Utc)
            endDate = endDate.ToUniversalTime();

        // Convert to Unspecified for PostgreSQL timestamp without time zone queries
        var startDateUnspecified = DateTime.SpecifyKind(startDate, DateTimeKind.Unspecified);
        var endDateUnspecified = DateTime.SpecifyKind(endDate, DateTimeKind.Unspecified);

        var results = new List<DailyInvoiceCount>();

        if (dateType == ImportDateType.InvoiceDate)
        {
            var rawResults = await _dbContext.IssuedInvoices
                .Where(i => i.InvoiceDate >= startDateUnspecified && i.InvoiceDate <= endDateUnspecified)
                .GroupBy(i => new { Year = i.InvoiceDate.Year, Month = i.InvoiceDate.Month, Day = i.InvoiceDate.Day })
                .Select(g => new
                {
                    Year = g.Key.Year,
                    Month = g.Key.Month,
                    Day = g.Key.Day,
                    Count = g.Count()
                })
                .OrderBy(d => new DateTime(d.Year, d.Month, d.Day))
                .ToListAsync(cancellationToken);

            results = rawResults.Select(r => new DailyInvoiceCount
            {
                Date = DateTime.SpecifyKind(new DateTime(r.Year, r.Month, r.Day), DateTimeKind.Utc),
                Count = r.Count,
                IsBelowThreshold = false
            }).ToList();
        }
        else
        {
            var rawResults = await _dbContext.IssuedInvoices
                .Where(i => i.LastSyncTime.HasValue &&
                           i.LastSyncTime.Value >= startDateUnspecified &&
                           i.LastSyncTime.Value <= endDateUnspecified)
                .GroupBy(i => new { Year = i.LastSyncTime.Value.Year, Month = i.LastSyncTime.Value.Month, Day = i.LastSyncTime.Value.Day })
                .Select(g => new
                {
                    Year = g.Key.Year,
                    Month = g.Key.Month,
                    Day = g.Key.Day,
                    Count = g.Count()
                })
                .OrderBy(d => new DateTime(d.Year, d.Month, d.Day))
                .ToListAsync(cancellationToken);

            results = rawResults.Select(r => new DailyInvoiceCount
            {
                Date = DateTime.SpecifyKind(new DateTime(r.Year, r.Month, r.Day), DateTimeKind.Utc),
                Count = r.Count,
                IsBelowThreshold = false
            }).ToList();
        }

        // Fill in missing dates with zero counts - work with UTC dates for consistency
        var filledResults = new List<DailyInvoiceCount>();
        var currentDate = DateTime.SpecifyKind(startDate.Date, DateTimeKind.Utc);
        var endDateOnly = DateTime.SpecifyKind(endDate.Date, DateTimeKind.Utc);

        while (currentDate <= endDateOnly)
        {
            var existingResult = results.FirstOrDefault(r => r.Date.Date == currentDate.Date);
            if (existingResult != null)
            {
                filledResults.Add(existingResult);
            }
            else
            {
                filledResults.Add(new DailyInvoiceCount
                {
                    Date = currentDate,
                    Count = 0,
                    IsBelowThreshold = false
                });
            }

            currentDate = currentDate.AddDays(1);
        }

        return filledResults;
    }

    /// <summary>
    /// Gets daily bank statement import statistics for monitoring purposes
    /// </summary>
    public async Task<List<DailyBankStatementStatistics>> GetBankStatementImportStatisticsAsync(
        DateTime startDate,
        DateTime endDate,
        BankStatementDateType dateType,
        CancellationToken cancellationToken = default)
    {
        // PostgreSQL timestamp without time zone: work with UTC dates but store as Unspecified
        // Convert input dates to UTC if needed, then to Unspecified for PostgreSQL compatibility
        if (startDate.Kind != DateTimeKind.Utc)
            startDate = startDate.ToUniversalTime();
        if (endDate.Kind != DateTimeKind.Utc)
            endDate = endDate.ToUniversalTime();

        // Convert to Unspecified for PostgreSQL timestamp without time zone queries
        var startDateUnspecified = DateTime.SpecifyKind(startDate, DateTimeKind.Unspecified);
        var endDateUnspecified = DateTime.SpecifyKind(endDate, DateTimeKind.Unspecified);

        var results = new List<DailyBankStatementStatistics>();

        if (dateType == BankStatementDateType.StatementDate)
        {
            var rawResults = await _dbContext.BankStatements
                .Where(b => b.StatementDate >= startDateUnspecified && b.StatementDate <= endDateUnspecified)
                .GroupBy(b => new { Year = b.StatementDate.Year, Month = b.StatementDate.Month, Day = b.StatementDate.Day })
                .Select(g => new
                {
                    Year = g.Key.Year,
                    Month = g.Key.Month,
                    Day = g.Key.Day,
                    ImportCount = g.Count(),
                    TotalItemCount = g.Sum(b => b.ItemCount)
                })
                .OrderBy(d => new DateTime(d.Year, d.Month, d.Day))
                .ToListAsync(cancellationToken);

            results = rawResults.Select(r => new DailyBankStatementStatistics
            {
                Date = DateTime.SpecifyKind(new DateTime(r.Year, r.Month, r.Day), DateTimeKind.Utc),
                ImportCount = r.ImportCount,
                TotalItemCount = r.TotalItemCount
            }).ToList();
        }
        else
        {
            var rawResults = await _dbContext.BankStatements
                .Where(b => b.ImportDate >= startDateUnspecified && b.ImportDate <= endDateUnspecified)
                .GroupBy(b => new { Year = b.ImportDate.Year, Month = b.ImportDate.Month, Day = b.ImportDate.Day })
                .Select(g => new
                {
                    Year = g.Key.Year,
                    Month = g.Key.Month,
                    Day = g.Key.Day,
                    ImportCount = g.Count(),
                    TotalItemCount = g.Sum(b => b.ItemCount)
                })
                .OrderBy(d => new DateTime(d.Year, d.Month, d.Day))
                .ToListAsync(cancellationToken);

            results = rawResults.Select(r => new DailyBankStatementStatistics
            {
                Date = DateTime.SpecifyKind(new DateTime(r.Year, r.Month, r.Day), DateTimeKind.Utc),
                ImportCount = r.ImportCount,
                TotalItemCount = r.TotalItemCount
            }).ToList();
        }

        // Fill in missing dates with zero counts - work with UTC dates for consistency
        var filledResults = new List<DailyBankStatementStatistics>();
        var currentDate = DateTime.SpecifyKind(startDate.Date, DateTimeKind.Utc);
        var endDateOnly = DateTime.SpecifyKind(endDate.Date, DateTimeKind.Utc);

        while (currentDate <= endDateOnly)
        {
            var existingResult = results.FirstOrDefault(r => r.Date.Date == currentDate.Date);
            if (existingResult != null)
            {
                filledResults.Add(existingResult);
            }
            else
            {
                filledResults.Add(new DailyBankStatementStatistics
                {
                    Date = currentDate,
                    ImportCount = 0,
                    TotalItemCount = 0
                });
            }

            currentDate = currentDate.AddDays(1);
        }

        return filledResults;
    }
}
