using Anela.Heblo.Domain.Features.Analytics;
using Anela.Heblo.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Anela.Heblo.Application.Features.Bank.Infrastructure;

internal sealed class BankStatementStatisticsSourceAdapter : IBankStatementStatisticsSource
{
    private readonly ApplicationDbContext _dbContext;

    public BankStatementStatisticsSourceAdapter(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<IReadOnlyList<DailyBankStatementStatistics>> GetDailyStatisticsAsync(
        DateTime startDate,
        DateTime endDate,
        BankStatementDateType dateType,
        CancellationToken cancellationToken = default)
    {
        if (startDate.Kind != DateTimeKind.Utc)
            startDate = startDate.ToUniversalTime();
        if (endDate.Kind != DateTimeKind.Utc)
            endDate = endDate.ToUniversalTime();

        var startDateUnspecified = DateTime.SpecifyKind(startDate, DateTimeKind.Unspecified);
        var endDateUnspecified = DateTime.SpecifyKind(endDate, DateTimeKind.Unspecified);

        List<DailyBankStatementStatistics> results;

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

        var resultsByDate = results.ToDictionary(r => r.Date.Date);
        var filledResults = new List<DailyBankStatementStatistics>();
        var currentDate = DateTime.SpecifyKind(startDate.Date, DateTimeKind.Utc);
        var endDateOnly = DateTime.SpecifyKind(endDate.Date, DateTimeKind.Utc);

        while (currentDate <= endDateOnly)
        {
            if (resultsByDate.TryGetValue(currentDate.Date, out var existingResult))
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
