using Anela.Heblo.Application.Features.FinancialOverview;
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

    private static (decimal income, decimal expenses) CalculatePeriodTotals(
        IEnumerable<LedgerItem> debitItems,
        IEnumerable<LedgerItem> creditItems)
    {
        var debit5 = debitItems.Where(i => i.DebitAccountNumber?.StartsWith("5") == true).Sum(i => i.Amount);
        var credit5 = creditItems.Where(i => i.CreditAccountNumber?.StartsWith("5") == true).Sum(i => i.Amount);
        var expenses = debit5 - credit5;

        var credit6 = creditItems.Where(i => i.CreditAccountNumber?.StartsWith("6") == true).Sum(i => i.Amount);
        var debit6 = debitItems.Where(i => i.DebitAccountNumber?.StartsWith("6") == true).Sum(i => i.Amount);
        var income = credit6 - debit6;

        return (income, expenses);
    }

    public async Task<GetFinancialOverviewResponse> GetFinancialOverviewAsync(
        int months,
        bool includeStockData,
        IReadOnlyList<string>? excludedDepartments = null,
        bool includeCurrentMonth = false,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Fetching financial overview for {Months} months, IncludeStock={IncludeStock}, ExcludedDepartments={ExcludedDepartments}, IncludeCurrentMonth={IncludeCurrentMonth}",
            months, includeStockData, excludedDepartments?.Count ?? 0, includeCurrentMonth);

        // Department filter always requires real-time (cache stores undivided totals per month)
        if (excludedDepartments is { Count: > 0 })
        {
            _logger.LogInformation("Department filter active ({Count} excluded), bypassing cache for real-time calculation",
                excludedDepartments.Count);
            return await GetFinancialOverviewRealTimeAsync(months, includeStockData, excludedDepartments, includeCurrentMonth, cancellationToken);
        }

        // Current month requested without department filter: use hybrid path
        // (cache for completed months + real-time for current partial month only)
        if (includeCurrentMonth)
        {
            _logger.LogInformation("Current month requested, using hybrid calculation (cache + real-time current month only)");
            try
            {
                var hybridCacheStatus = GetCacheStatus();
                if (hybridCacheStatus.CachedMonthsCount == 0)
                {
                    _logger.LogInformation("Cache is empty, falling back to full real-time calculation for current month request");
                    return await GetFinancialOverviewRealTimeAsync(months, includeStockData, null, true, cancellationToken);
                }
                return await GetHybridWithCurrentMonthAsync(months, includeStockData, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Hybrid current-month calculation failed, falling back to full real-time");
                return await GetFinancialOverviewRealTimeAsync(months, includeStockData, null, true, cancellationToken);
            }
        }

        try
        {
            var cacheStatus = GetCacheStatus();

            if (cacheStatus.CachedMonthsCount == 0)
            {
                _logger.LogInformation("Cache is empty, using real-time calculation");
                return await GetFinancialOverviewRealTimeAsync(months, includeStockData, null, false, cancellationToken);
            }

            var cachedResponse = GetCachedFinancialOverview(months, includeStockData);

            _logger.LogInformation("Using cached financial data - Last refresh: {LastRefresh}, Cached months: {CachedMonths}",
                cacheStatus.LastRefresh, cacheStatus.CachedMonthsCount);

            return cachedResponse;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get cached financial data, falling back to real-time calculation");

            // Fallback to real-time calculation if cache fails
            return await GetFinancialOverviewRealTimeAsync(months, includeStockData, null, false, cancellationToken);
        }
    }

    public async Task<GetFinancialComparisonResponse> GetFinancialComparisonAsync(
        int years,
        bool includeStockData,
        IReadOnlyList<string>? excludedDepartments,
        bool includePartialMonth,
        CancellationToken cancellationToken = default)
    {
        var n = Math.Clamp(years, 2, 3);
        var cutoffDate = DateTime.UtcNow.Date.AddDays(-_options.PartialMonthLagDays);
        var anchorYear = cutoffDate.Year;
        var partialMonth = cutoffDate.Month;
        var cutoffDay = cutoffDate.Day;

        var excludedSet = excludedDepartments is { Count: > 0 }
            ? excludedDepartments.ToHashSet(StringComparer.OrdinalIgnoreCase)
            : null;

        _logger.LogInformation(
            "Financial comparison: {Years} years, anchor {AnchorYear}, cutoff {Cutoff:d}, includeStock={Stock}, includePartial={Partial}, deptFilter={Filter}",
            n, anchorYear, cutoffDate, includeStockData, includePartialMonth, excludedSet != null);

        try
        {
            var yearsList = Enumerable.Range(0, n).Select(i => anchorYear - i).ToList();

            var series = new List<YearComparisonSeriesDto>();
            foreach (var year in yearsList)
            {
                var months = await BuildComparisonYearAsync(
                    year, anchorYear, partialMonth, cutoffDay, cutoffDate,
                    includeStockData, includePartialMonth, excludedSet, cancellationToken);
                series.Add(BuildYearSeries(year, months, partialMonth, includeStockData));
            }

            return new GetFinancialComparisonResponse
            {
                Series = series,
                Metadata = new FinancialComparisonMetadataDto
                {
                    CutoffDate = cutoffDate,
                    AnchorYear = anchorYear,
                    PartialMonth = partialMonth,
                    PartialDayOfMonth = cutoffDay,
                    Years = yearsList,
                    IncludeStockData = includeStockData,
                    PartialMonthIncluded = includePartialMonth,
                    LagDays = _options.PartialMonthLagDays
                }
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to build financial comparison for anchor year {AnchorYear}", anchorYear);
            throw;
        }
    }

    private async Task<List<MonthlyFinancialDataDto>> BuildComparisonYearAsync(
        int year, int anchorYear, int partialMonth, int cutoffDay, DateTime cutoffDate,
        bool includeStockData, bool includePartialMonth, HashSet<string>? excludedSet,
        CancellationToken cancellationToken)
    {
        var cells = new List<MonthlyFinancialDataDto>();

        for (int month = 1; month <= 12; month++)
        {
            // Anchor-year months after the partial month are still in the future - no data.
            if (year == anchorYear && month > partialMonth)
                continue;

            var isPartial = month == partialMonth;
            if (isPartial && !includePartialMonth)
                continue;

            var cell = await BuildComparisonCellAsync(
                year, month, isPartial, cutoffDay, cutoffDate,
                includeStockData, excludedSet, cancellationToken);
            cells.Add(cell);
        }

        return cells; // ascending by month
    }

    private async Task<MonthlyFinancialDataDto> BuildComparisonCellAsync(
        int year, int month, bool isPartial, int cutoffDay, DateTime cutoffDate,
        bool includeStockData, HashSet<string>? excludedSet,
        CancellationToken cancellationToken)
    {
        var monthStart = new DateTime(year, month, 1);
        var daysInMonth = DateTime.DaysInMonth(year, month);
        var effectiveDay = isPartial ? Math.Min(cutoffDay, daysInMonth) : daysInMonth;

        // Anchor-year partial cell ends exactly on the cutoff date (matches its time component of 00:00).
        var periodEnd = isPartial ? new DateTime(year, month, effectiveDay) : new DateTime(year, month, daysInMonth);

        // Completed full months without a department filter can be served from the existing cache.
        if (!isPartial && excludedSet == null &&
            TryGetCachedComparisonMonth(year, month, includeStockData, out var cachedCell))
        {
            return cachedCell!;
        }

        // Live single-month computation (also the path for partial cells and department-filtered requests).
        var debitTask = _ledgerService.GetLedgerItems(
            monthStart, periodEnd, debitAccountPrefix: new[] { "5", "6" }, cancellationToken: cancellationToken);
        var creditTask = _ledgerService.GetLedgerItems(
            monthStart, periodEnd, creditAccountPrefix: new[] { "5", "6" }, cancellationToken: cancellationToken);

        await Task.WhenAll(debitTask, creditTask);

        var debitItems = FilterExcludedDepartments(await debitTask, excludedSet);
        var creditItems = FilterExcludedDepartments(await creditTask, excludedSet);

        var (income, expenses) = CalculatePeriodTotals(debitItems, creditItems);

        MonthlyStockChange? stockChange = null;
        if (includeStockData)
        {
            stockChange = isPartial
                ? await _stockValueService.GetStockValueChangeForPeriodAsync(monthStart, periodEnd, cancellationToken)
                : (await _stockValueService.GetStockValueChangesAsync(monthStart, periodEnd, cancellationToken))
                    .FirstOrDefault();
        }

        var dto = MapToDto(year, month, income, expenses, stockChange, includeStockData);
        if (isPartial)
        {
            dto.IsPartial = true;
            dto.PartialDayOfMonth = effectiveDay;
        }
        return dto;
    }

    private bool TryGetCachedComparisonMonth(
        int year, int month, bool includeStockData, out MonthlyFinancialDataDto? dto)
    {
        dto = null;

        var monthlyKey = $"{MONTHLY_DATA_CACHE_KEY_PREFIX}{year}_{month}";
        if (!_memoryCache.TryGetValue(monthlyKey, out var value) || value is not MonthlyFinancialData data)
            return false;

        MonthlyStockChange? stock = null;
        if (includeStockData)
        {
            var stockKey = $"{STOCK_DATA_CACHE_KEY_PREFIX}{year}_{month}";
            if (_memoryCache.TryGetValue(stockKey, out var s) && s is MonthlyStockChange sc)
                stock = sc;
        }

        dto = MapToDto(data.Year, data.Month, data.Income, data.Expenses, stock, includeStockData);
        return true;
    }

    private static IEnumerable<LedgerItem> FilterExcludedDepartments(
        IList<LedgerItem> items, HashSet<string>? excludedSet)
        => excludedSet == null
            ? items
            : items.Where(item => item.Department == null || !excludedSet.Contains(item.Department));

    private static YearComparisonSeriesDto BuildYearSeries(
        int year, List<MonthlyFinancialDataDto> months, int partialMonth, bool includeStockData)
    {
        var ytdCells = months.Where(m => m.Month <= partialMonth).ToList();

        return new YearComparisonSeriesDto
        {
            Year = year,
            Months = months,
            YtdIncome = ytdCells.Sum(m => m.Income),
            YtdExpenses = ytdCells.Sum(m => m.Expenses),
            YtdFinancialBalance = ytdCells.Sum(m => m.FinancialBalance),
            YtdStockValueChange = includeStockData ? ytdCells.Sum(m => m.TotalStockValueChange ?? 0) : (decimal?)null,
            YtdTotalBalance = includeStockData ? ytdCells.Sum(m => m.TotalBalance ?? m.FinancialBalance) : (decimal?)null
        };
    }

    public async Task RefreshFinancialDataAsync(
        DateTime? startDate,
        DateTime? endDate,
        CancellationToken cancellationToken = default)
    {
        var lastRefresh = _memoryCache.Get<DateTime?>(LAST_REFRESH_CACHE_KEY) ?? DateTime.MinValue;
        if (DateTime.UtcNow - lastRefresh < TimeSpan.FromMinutes(10)) // Prevent too frequent refreshes
        {
            _logger.LogDebug("Skipping refresh, last refresh was too recent");
            return;
        }

        try
        {
            endDate ??= new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1).AddDays(-1); // Last day of previous month
            startDate ??= endDate.Value.AddMonths(-_options.MonthsToCache + 1);

            _logger.LogInformation("Starting financial analysis data refresh from {StartDate} to {EndDate}", startDate, endDate);

            // Load financial data month by month sequentially from newest to oldest
            // to avoid overloading the target system
            var currentDate = endDate.Value;

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
            var (income, expenses) = CalculatePeriodTotals(debitItems, creditItems);

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

    private async Task<GetFinancialOverviewResponse> GetHybridWithCurrentMonthAsync(
        int months,
        bool includeStockData,
        CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        var currentMonthStart = new DateTime(now.Year, now.Month, 1);
        var currentMonthEnd = now.Date;

        _logger.LogInformation(
            "Fetching current month ({Year}-{Month:D2}) data in real-time, serving {CompletedMonths} completed month(s) from cache",
            now.Year, now.Month, months - 1);

        // Fetch only the current (partial) month in parallel — narrow query instead of full date range
        var debitItemsTask = _ledgerService.GetLedgerItems(
            currentMonthStart,
            currentMonthEnd,
            debitAccountPrefix: new[] { "5", "6" },
            cancellationToken: cancellationToken);

        var creditItemsTask = _ledgerService.GetLedgerItems(
            currentMonthStart,
            currentMonthEnd,
            creditAccountPrefix: new[] { "5", "6" },
            cancellationToken: cancellationToken);

        var stockChangesTask = includeStockData
            ? _stockValueService.GetStockValueChangesAsync(currentMonthStart, currentMonthEnd, cancellationToken)
            : Task.FromResult<IReadOnlyList<MonthlyStockChange>>(new List<MonthlyStockChange>());

        await Task.WhenAll(debitItemsTask, creditItemsTask, stockChangesTask);

        var debitItems = await debitItemsTask;
        var creditItems = await creditItemsTask;
        var stockChanges = await stockChangesTask;

        // Compute income/expenses for current month
        var (income, expenses) = CalculatePeriodTotals(debitItems, creditItems);

        var stockChange = stockChanges.FirstOrDefault();
        var currentMonthDto = MapToDto(now.Year, now.Month, income, expenses, stockChange, includeStockData);

        // Get cached data for completed months (months-1 most recent completed months)
        var completedData = months > 1
            ? GetCachedFinancialOverview(months - 1, includeStockData).Data
            : new List<MonthlyFinancialDataDto>();

        // Merge: current month first (most recent), then completed months in descending order
        var allData = new List<MonthlyFinancialDataDto> { currentMonthDto };
        allData.AddRange(completedData);

        _logger.LogInformation(
            "Hybrid financial overview ready: 1 real-time month + {CompletedCount} cached month(s)",
            completedData.Count);

        return new GetFinancialOverviewResponse
        {
            Data = allData,
            Summary = BuildSummary(allData, includeStockData)
        };
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

            monthlyData.Add(MapToDto(
                cachedFinancialData.Year,
                cachedFinancialData.Month,
                cachedFinancialData.Income,
                cachedFinancialData.Expenses,
                cachedStockData,
                includeStockData));

            currentDate = currentDate.AddMonths(1);
        }

        var orderedData = monthlyData.OrderByDescending(d => d.Year).ThenByDescending(d => d.Month).ToList();

        return new GetFinancialOverviewResponse
        {
            Data = orderedData,
            Summary = BuildSummary(orderedData, includeStockData)
        };
    }

    private async Task<GetFinancialOverviewResponse> GetFinancialOverviewRealTimeAsync(
        int months,
        bool includeStockData,
        IReadOnlyList<string>? excludedDepartments,
        bool includeCurrentMonth,
        CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        // When including current month, use today as end date; otherwise last day of previous month
        var endDate = includeCurrentMonth
            ? now.Date
            : new DateTime(now.Year, now.Month, 1).AddDays(-1);

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

        var allDebitItems = await debitItemsTask;
        var allCreditItems = await creditItemsTask;
        var stockChanges = await stockChangesTask;

        // Apply department filtering in-memory when departments are excluded
        var excludedSet = excludedDepartments is { Count: > 0 }
            ? excludedDepartments.ToHashSet(StringComparer.OrdinalIgnoreCase)
            : null;

        var debitItems = excludedSet != null
            ? allDebitItems.Where(item => item.Department == null || !excludedSet.Contains(item.Department)).ToList()
            : allDebitItems;

        var creditItems = excludedSet != null
            ? allCreditItems.Where(item => item.Department == null || !excludedSet.Contains(item.Department)).ToList()
            : allCreditItems;

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

            var (income, expenses) = CalculatePeriodTotals(monthDebitItems, monthCreditItems);

            monthlyData.Add(new MonthlyFinancialData
            {
                Year = currentDate.Year,
                Month = currentDate.Month,
                Income = income,
                Expenses = expenses
            });

            currentDate = currentDate.AddMonths(1);
        }

        var orderedData = monthlyData.OrderByDescending(d => d.Year).ThenByDescending(d => d.Month)
            .Select(d =>
            {
                var stockChangeData = stockChangesLookup.TryGetValue(new { d.Year, d.Month }, out var stockChange)
                    ? stockChange
                    : null;
                return MapToDto(d.Year, d.Month, d.Income, d.Expenses, stockChangeData, includeStockData);
            }).ToList();

        var response = new GetFinancialOverviewResponse
        {
            Data = orderedData,
            Summary = BuildSummary(orderedData, includeStockData)
        };

        _logger.LogInformation("Real-time financial overview generated with {Count} months of data", response.Data.Count);

        return response;
    }

    private static FinancialSummaryDto BuildSummary(List<MonthlyFinancialDataDto> data, bool includeStockData)
    {
        return new FinancialSummaryDto
        {
            TotalIncome = data.Sum(d => d.Income),
            TotalExpenses = data.Sum(d => d.Expenses),
            TotalBalance = data.Sum(d => d.FinancialBalance),
            AverageMonthlyIncome = data.Any() ? data.Average(d => d.Income) : 0,
            AverageMonthlyExpenses = data.Any() ? data.Average(d => d.Expenses) : 0,
            AverageMonthlyBalance = data.Any() ? data.Average(d => d.FinancialBalance) : 0,
            StockSummary = includeStockData ? CreateStockSummary(data) : null
        };
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

    private static MonthlyFinancialDataDto MapToDto(
        int year,
        int month,
        decimal income,
        decimal expenses,
        MonthlyStockChange? stockChange,
        bool includeStockData)
    {
        var financialBalance = income - expenses;
        return new MonthlyFinancialDataDto
        {
            Year = year,
            Month = month,
            MonthYearDisplay = $"{month:D2}/{year}",
            Income = income,
            Expenses = expenses,
            FinancialBalance = financialBalance,
            StockChanges = includeStockData && stockChange != null
                ? new StockChangeDto
                {
                    Materials = stockChange.StockChanges.Materials,
                    SemiProducts = stockChange.StockChanges.SemiProducts,
                    Products = stockChange.StockChanges.Products
                }
                : null,
            TotalStockValueChange = includeStockData
                ? (stockChange?.TotalStockValueChange ?? 0)
                : null,
            TotalBalance = includeStockData
                ? financialBalance + (stockChange?.TotalStockValueChange ?? 0)
                : null
        };
    }
}

public record MonthYearKey(int Year, int Month);