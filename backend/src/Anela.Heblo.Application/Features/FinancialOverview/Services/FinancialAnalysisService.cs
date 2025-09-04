using Anela.Heblo.Application.Features.FinancialOverview.Model;
using Anela.Heblo.Domain.Accounting.Ledger;
using Anela.Heblo.Domain.Features.FinancialOverview;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Anela.Heblo.Application.Features.FinancialOverview.Services;

public class FinancialAnalysisService : IFinancialAnalysisService
{
    private readonly ILedgerService _ledgerService;
    private readonly IStockValueService _stockValueService;
    private readonly ILogger<FinancialAnalysisService> _logger;
    private readonly FinancialAnalysisOptions _options;
    private readonly IMemoryCache _memoryCache;

    private const string MONTHLY_DATA_CACHE_KEY_PREFIX = "financial_monthly_data_";
    private const string STOCK_DATA_CACHE_KEY_PREFIX = "financial_stock_data_";
    private const string LAST_REFRESH_CACHE_KEY = "financial_last_refresh";

    private readonly object _refreshLock = new();

    public FinancialAnalysisService(
        ILedgerService ledgerService,
        IStockValueService stockValueService,
        ILogger<FinancialAnalysisService> logger,
        IOptions<FinancialAnalysisOptions> options,
        IMemoryCache memoryCache)
    {
        _ledgerService = ledgerService;
        _stockValueService = stockValueService;
        _logger = logger;
        _options = options.Value;
        _memoryCache = memoryCache;
    }

    public async Task<GetFinancialOverviewResponse> GetFinancialOverviewAsync(
        int months,
        bool includeStockData,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Fetching financial overview for {Months} months, IncludeStock={IncludeStock}",
            months, includeStockData);

        try
        {
            // First, try to get data from cache
            var cachedResponse = GetCachedFinancialOverview(months, includeStockData);

            var cacheStatus = GetCacheStatus();
            _logger.LogInformation("Using cached financial data - Last refresh: {LastRefresh}, Cached months: {CachedMonths}",
                cacheStatus.LastRefresh, cacheStatus.CachedMonthsCount);

            return cachedResponse;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get cached financial data, falling back to real-time calculation");

            // Fallback to real-time calculation if cache fails
            return await GetFinancialOverviewRealTimeAsync(months, includeStockData, cancellationToken);
        }
    }

    public async Task RefreshFinancialDataAsync(
        DateTime startDate,
        DateTime endDate,
        CancellationToken cancellationToken = default)
    {
        lock (_refreshLock)
        {
            var lastRefresh = _memoryCache.Get<DateTime?>(LAST_REFRESH_CACHE_KEY) ?? DateTime.MinValue;
            if (DateTime.UtcNow - lastRefresh < TimeSpan.FromMinutes(10)) // Prevent too frequent refreshes
            {
                _logger.LogDebug("Skipping refresh, last refresh was too recent");
                return;
            }
        }

        try
        {
            _logger.LogInformation("Starting financial analysis data refresh from {StartDate} to {EndDate}",
                startDate, endDate);

            // Load financial data month by month sequentially from newest to oldest
            // to avoid overloading the target system
            var currentDate = endDate;

            while (currentDate >= startDate)
            {
                var monthStart = new DateTime(currentDate.Year, currentDate.Month, 1);
                var monthEnd = monthStart.AddMonths(1).AddDays(-1);

                // Process sequentially to avoid system overload
                await RefreshMonthlyDataAsync(monthStart, monthEnd, cancellationToken);

                // Move to previous month
                currentDate = new DateTime(currentDate.Year, currentDate.Month, 1).AddMonths(-1);
            }

            // Cache the last refresh time
            _memoryCache.Set(LAST_REFRESH_CACHE_KEY, DateTime.UtcNow, TimeSpan.FromHours(24));

            _logger.LogInformation("Financial analysis data refresh completed successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to refresh financial analysis data");
            throw;
        }
    }

    public FinancialAnalysisCacheStatus GetCacheStatus()
    {
        var lastRefresh = _memoryCache.Get<DateTime?>(LAST_REFRESH_CACHE_KEY) ?? DateTime.MinValue;

        // Count cached monthly data by attempting to get cache statistics
        // Since IMemoryCache doesn't provide a direct count, we'll estimate based on recent months
        var estimatedCachedMonths = 0;
        var estimatedCachedStockMonths = 0;

        // Check last 24 months to estimate cache contents
        var currentDate = DateTime.UtcNow;
        for (int i = 0; i < 24; i++)
        {
            var checkDate = currentDate.AddMonths(-i);
            var key = new MonthYearKey(checkDate.Year, checkDate.Month);

            if (_memoryCache.TryGetValue($"{MONTHLY_DATA_CACHE_KEY_PREFIX}{key.Year}_{key.Month}", out _))
                estimatedCachedMonths++;

            if (_memoryCache.TryGetValue($"{STOCK_DATA_CACHE_KEY_PREFIX}{key.Year}_{key.Month}", out _))
                estimatedCachedStockMonths++;
        }

        return new FinancialAnalysisCacheStatus
        {
            LastRefresh = lastRefresh,
            CachedMonthsCount = estimatedCachedMonths,
            CachedStockMonthsCount = estimatedCachedStockMonths,
            NextRefreshDue = lastRefresh.Add(_options.RefreshInterval)
        };
    }

    private async Task RefreshMonthlyDataAsync(
        DateTime monthStart,
        DateTime monthEnd,
        CancellationToken cancellationToken)
    {
        try
        {
            var key = new MonthYearKey(monthStart.Year, monthStart.Month);

            _logger.LogDebug("Refreshing data for {Year}-{Month:D2}", key.Year, key.Month);

            // Fetch ledger data for this month in parallel
            var debitItemsTask = _ledgerService.GetLedgerItems(
                monthStart,
                monthEnd,
                debitAccountPrefix: new[] { "5", "6" },
                cancellationToken: cancellationToken);

            var creditItemsTask = _ledgerService.GetLedgerItems(
                monthStart,
                monthEnd,
                creditAccountPrefix: new[] { "5", "6" },
                cancellationToken: cancellationToken);

            var stockChangesTask = _stockValueService.GetStockValueChangesAsync(
                monthStart, monthEnd, cancellationToken);

            await Task.WhenAll(debitItemsTask, creditItemsTask, stockChangesTask);

            var debitItems = await debitItemsTask;
            var creditItems = await creditItemsTask;
            var stockChanges = await stockChangesTask;

            // Calculate financial data for this month
            var debit5 = debitItems
                .Where(item => item.DebitAccountNumber?.StartsWith("5") == true)
                .Sum(item => item.Amount);
            var credit5 = creditItems
                .Where(item => item.CreditAccountNumber?.StartsWith("5") == true)
                .Sum(item => item.Amount);
            var expenses = debit5 - credit5;

            var credit6 = creditItems
                .Where(item => item.CreditAccountNumber?.StartsWith("6") == true)
                .Sum(item => item.Amount);
            var debit6 = debitItems
                .Where(item => item.DebitAccountNumber?.StartsWith("6") == true)
                .Sum(item => item.Amount);
            var income = credit6 - debit6;

            // Cache the monthly financial data
            var monthlyData = new MonthlyFinancialData
            {
                Year = key.Year,
                Month = key.Month,
                Income = income,
                Expenses = expenses
            };

            // Cache the monthly financial data with 24 hour expiration
            var monthlyDataCacheKey = $"{MONTHLY_DATA_CACHE_KEY_PREFIX}{key.Year}_{key.Month}";
            _memoryCache.Set(monthlyDataCacheKey, monthlyData, TimeSpan.FromHours(24));

            // Cache stock data if available
            var stockChange = stockChanges.FirstOrDefault();
            if (stockChange != null)
            {
                var stockDataCacheKey = $"{STOCK_DATA_CACHE_KEY_PREFIX}{key.Year}_{key.Month}";
                _memoryCache.Set(stockDataCacheKey, stockChange, TimeSpan.FromHours(24));
            }

            _logger.LogDebug("Cached data for {Year}-{Month:D2}: Income={Income:C}, Expenses={Expenses:C}",
                key.Year, key.Month, income, expenses);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to refresh monthly data for {Year}-{Month:D2}",
                monthStart.Year, monthStart.Month);
        }
    }

    private GetFinancialOverviewResponse GetCachedFinancialOverview(int months, bool includeStockData)
    {
        var endDate = new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1).AddDays(-1);
        var startDate = endDate.AddMonths(-months + 1);
        startDate = new DateTime(startDate.Year, startDate.Month, 1);

        var monthlyData = new List<MonthlyFinancialDataDto>();
        var currentDate = startDate;

        while (currentDate <= endDate)
        {
            var key = new MonthYearKey(currentDate.Year, currentDate.Month);

            // Get cached data or return zeros if not available
            var monthlyDataCacheKey = $"{MONTHLY_DATA_CACHE_KEY_PREFIX}{key.Year}_{key.Month}";
            var cachedFinancialData = _memoryCache.TryGetValue(monthlyDataCacheKey, out var financialData) && financialData is MonthlyFinancialData data
                ? data
                : new MonthlyFinancialData { Year = key.Year, Month = key.Month, Income = 0, Expenses = 0 };

            MonthlyStockChange? cachedStockData = null;
            if (includeStockData)
            {
                var stockDataCacheKey = $"{STOCK_DATA_CACHE_KEY_PREFIX}{key.Year}_{key.Month}";
                if (_memoryCache.TryGetValue(stockDataCacheKey, out var stockData) && stockData is MonthlyStockChange stockChange)
                {
                    cachedStockData = stockChange;
                }
            }

            monthlyData.Add(new MonthlyFinancialDataDto
            {
                Year = cachedFinancialData.Year,
                Month = cachedFinancialData.Month,
                MonthYearDisplay = cachedFinancialData.MonthYearDisplay,
                Income = cachedFinancialData.Income,
                Expenses = cachedFinancialData.Expenses,
                FinancialBalance = cachedFinancialData.FinancialBalance,
                StockChanges = includeStockData && cachedStockData != null ? new StockChangeDto
                {
                    Materials = cachedStockData.StockChanges.Materials,
                    SemiProducts = cachedStockData.StockChanges.SemiProducts,
                    Products = cachedStockData.StockChanges.Products
                } : null,
                TotalStockValueChange = includeStockData ? (cachedStockData?.TotalStockValueChange ?? 0) : null,
                TotalBalance = includeStockData
                    ? cachedFinancialData.FinancialBalance + (cachedStockData?.TotalStockValueChange ?? 0)
                    : null
            });

            currentDate = currentDate.AddMonths(1);
        }

        var orderedData = monthlyData.OrderByDescending(d => d.Year).ThenByDescending(d => d.Month).ToList();

        return new GetFinancialOverviewResponse
        {
            Data = orderedData,
            Summary = new FinancialSummaryDto
            {
                TotalIncome = orderedData.Sum(d => d.Income),
                TotalExpenses = orderedData.Sum(d => d.Expenses),
                TotalBalance = orderedData.Sum(d => d.FinancialBalance),
                AverageMonthlyIncome = orderedData.Any() ? orderedData.Average(d => d.Income) : 0,
                AverageMonthlyExpenses = orderedData.Any() ? orderedData.Average(d => d.Expenses) : 0,
                AverageMonthlyBalance = orderedData.Any() ? orderedData.Average(d => d.FinancialBalance) : 0,
                StockSummary = includeStockData ? CreateStockSummary(orderedData) : null
            }
        };
    }

    private async Task<GetFinancialOverviewResponse> GetFinancialOverviewRealTimeAsync(
        int months,
        bool includeStockData,
        CancellationToken cancellationToken)
    {
        // Set endDate to last day of previous month (exclude current month)
        var now = DateTime.UtcNow;
        var endDate = new DateTime(now.Year, now.Month, 1).AddDays(-1);

        // Calculate startDate - go back the requested number of months from the end date
        var startDate = endDate.AddMonths(-months + 1);
        startDate = new DateTime(startDate.Year, startDate.Month, 1);

        _logger.LogInformation("Performing real-time financial calculation for {Months} months from {StartDate} to {EndDate}",
            months, startDate, endDate);

        // Fetch financial data and stock data in parallel if needed
        var debitItemsTask = _ledgerService.GetLedgerItems(
            startDate,
            endDate,
            debitAccountPrefix: new[] { "5", "6" },
            cancellationToken: cancellationToken);

        var creditItemsTask = _ledgerService.GetLedgerItems(
            startDate,
            endDate,
            creditAccountPrefix: new[] { "5", "6" },
            cancellationToken: cancellationToken);

        var stockChangesTask = includeStockData
            ? _stockValueService.GetStockValueChangesAsync(startDate, endDate, cancellationToken)
            : Task.FromResult<IReadOnlyList<MonthlyStockChange>>(new List<MonthlyStockChange>());

        // Execute queries in parallel
        await Task.WhenAll(debitItemsTask, creditItemsTask, stockChangesTask);

        var debitItems = await debitItemsTask;
        var creditItems = await creditItemsTask;
        var stockChanges = await stockChangesTask;

        // Create lookup for stock changes by year/month for efficient access
        var stockChangesList = stockChanges.ToList();
        var stockChangesLookup = stockChangesList.ToDictionary(sc => new { sc.Year, sc.Month }, sc => sc);

        var monthlyData = new List<MonthlyFinancialData>();
        var currentDate = startDate;

        while (currentDate <= endDate)
        {
            var monthStart = new DateTime(currentDate.Year, currentDate.Month, 1);
            var monthEnd = monthStart.AddMonths(1).AddDays(-1);

            // Filter items for this month
            var monthDebitItems = debitItems.Where(item => item.Date >= monthStart && item.Date <= monthEnd);
            var monthCreditItems = creditItems.Where(item => item.Date >= monthStart && item.Date <= monthEnd);

            // Calculate expenses: debit(5) - credit(5)
            var debit5 = monthDebitItems
                .Where(item => item.DebitAccountNumber?.StartsWith("5") == true)
                .Sum(item => item.Amount);
            var credit5 = monthCreditItems
                .Where(item => item.CreditAccountNumber?.StartsWith("5") == true)
                .Sum(item => item.Amount);
            var expenses = debit5 - credit5;

            // Calculate income: credit(6) - debit(6)
            var credit6 = monthCreditItems
                .Where(item => item.CreditAccountNumber?.StartsWith("6") == true)
                .Sum(item => item.Amount);
            var debit6 = monthDebitItems
                .Where(item => item.DebitAccountNumber?.StartsWith("6") == true)
                .Sum(item => item.Amount);
            var income = credit6 - debit6;

            monthlyData.Add(new MonthlyFinancialData
            {
                Year = currentDate.Year,
                Month = currentDate.Month,
                Income = income,
                Expenses = expenses
            });

            currentDate = currentDate.AddMonths(1);
        }

        var response = new GetFinancialOverviewResponse
        {
            Data = monthlyData.OrderByDescending(d => d.Year).ThenByDescending(d => d.Month)
                .Select(d =>
                {
                    var stockChangeData = stockChangesLookup.TryGetValue(new { d.Year, d.Month }, out var stockChange)
                        ? stockChange
                        : null;

                    return new MonthlyFinancialDataDto
                    {
                        Year = d.Year,
                        Month = d.Month,
                        MonthYearDisplay = d.MonthYearDisplay,
                        Income = d.Income,
                        Expenses = d.Expenses,
                        FinancialBalance = d.FinancialBalance,
                        StockChanges = includeStockData && stockChangeData != null ? new StockChangeDto
                        {
                            Materials = stockChangeData.StockChanges.Materials,
                            SemiProducts = stockChangeData.StockChanges.SemiProducts,
                            Products = stockChangeData.StockChanges.Products
                        } : null,
                        TotalStockValueChange = includeStockData ? stockChangeData?.TotalStockValueChange : null,
                        TotalBalance = includeStockData
                            ? d.FinancialBalance + (stockChangeData?.TotalStockValueChange ?? 0)
                            : null
                    };
                }).ToList(),
            Summary = new FinancialSummaryDto
            {
                TotalIncome = monthlyData.Sum(d => d.Income),
                TotalExpenses = monthlyData.Sum(d => d.Expenses),
                TotalBalance = monthlyData.Sum(d => d.FinancialBalance),
                AverageMonthlyIncome = monthlyData.Any() ? monthlyData.Average(d => d.Income) : 0,
                AverageMonthlyExpenses = monthlyData.Any() ? monthlyData.Average(d => d.Expenses) : 0,
                AverageMonthlyBalance = monthlyData.Any() ? monthlyData.Average(d => d.FinancialBalance) : 0,
                StockSummary = includeStockData ? CreateStockSummary(monthlyData, stockChangesList) : null
            }
        };

        _logger.LogInformation("Real-time financial overview generated with {Count} months of data", response.Data.Count);

        return response;
    }

    private static StockSummaryDto CreateStockSummary(List<MonthlyFinancialDataDto> monthlyData)
    {
        var totalStockChange = monthlyData.Sum(d => d.TotalStockValueChange ?? 0);
        var averageStockChange = monthlyData.Any() ? monthlyData.Average(d => d.TotalStockValueChange ?? 0) : 0;
        var totalFinancialBalance = monthlyData.Sum(d => d.FinancialBalance);
        var averageFinancialBalance = monthlyData.Any() ? monthlyData.Average(d => d.FinancialBalance) : 0;

        return new StockSummaryDto
        {
            TotalStockValueChange = totalStockChange,
            AverageMonthlyStockChange = averageStockChange,
            TotalBalanceWithStock = totalFinancialBalance + totalStockChange,
            AverageMonthlyTotalBalance = averageFinancialBalance + averageStockChange
        };
    }

    private static StockSummaryDto CreateStockSummary(
        List<MonthlyFinancialData> monthlyData,
        List<MonthlyStockChange> stockChanges)
    {
        var totalStockChange = stockChanges.Sum(sc => (decimal)sc.TotalStockValueChange);
        var averageStockChange = stockChanges.Any() ? stockChanges.Average(sc => (decimal)sc.TotalStockValueChange) : 0;
        var totalFinancialBalance = monthlyData.Sum(d => d.FinancialBalance);
        var averageFinancialBalance = monthlyData.Any() ? monthlyData.Average(d => d.FinancialBalance) : 0;

        return new StockSummaryDto
        {
            TotalStockValueChange = totalStockChange,
            AverageMonthlyStockChange = averageStockChange,
            TotalBalanceWithStock = totalFinancialBalance + totalStockChange,
            AverageMonthlyTotalBalance = averageFinancialBalance + averageStockChange
        };
    }
}

public record MonthYearKey(int Year, int Month);