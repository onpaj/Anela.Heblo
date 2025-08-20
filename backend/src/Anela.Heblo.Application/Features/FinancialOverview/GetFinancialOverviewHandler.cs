using Anela.Heblo.Application.Features.FinancialOverview.Model;
using Anela.Heblo.Domain.Accounting.Ledger;
using Anela.Heblo.Domain.Features.FinancialOverview;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.FinancialOverview;

public class GetFinancialOverviewHandler : IRequestHandler<GetFinancialOverviewRequest, GetFinancialOverviewResponse>
{
    private readonly ILedgerService _ledgerService;
    private readonly IStockValueService _stockValueService;
    private readonly ILogger<GetFinancialOverviewHandler> _logger;
    
    public GetFinancialOverviewHandler(
        ILedgerService ledgerService,
        IStockValueService stockValueService,
        ILogger<GetFinancialOverviewHandler> logger)
    {
        _ledgerService = ledgerService;
        _stockValueService = stockValueService;
        _logger = logger;
    }
    
    public async Task<GetFinancialOverviewResponse> Handle(GetFinancialOverviewRequest request, CancellationToken cancellationToken)
    {
        var months = request.Months ?? 6;
        
        // Set endDate to last day of previous month (exclude current month)
        var now = DateTime.UtcNow;
        var endDate = new DateTime(now.Year, now.Month, 1).AddDays(-1); // Last day of previous month
        
        // Calculate startDate - go back the requested number of months from the end date
        var startDate = endDate.AddMonths(-months + 1);
        startDate = new DateTime(startDate.Year, startDate.Month, 1); // First day of start month
        
        _logger.LogInformation("Fetching financial overview for {Months} months from {StartDate} to {EndDate}, IncludeStock={IncludeStock}", 
            months, startDate, endDate, request.IncludeStockData);
        
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

        var stockChangesTask = request.IncludeStockData 
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
                    var stockChangeData = stockChangesLookup.TryGetValue(new { d.Year, d.Month }, out var stockChange) ? stockChange : null;
                    
                    return new MonthlyFinancialDataDto
                    {
                        Year = d.Year,
                        Month = d.Month,
                        MonthYearDisplay = d.MonthYearDisplay,
                        Income = d.Income,
                        Expenses = d.Expenses,
                        FinancialBalance = d.FinancialBalance,
                        StockChanges = request.IncludeStockData && stockChangeData != null ? new StockChangeDto
                        {
                            Materials = stockChangeData.StockChanges.Materials,
                            SemiProducts = stockChangeData.StockChanges.SemiProducts,
                            Products = stockChangeData.StockChanges.Products
                        } : null,
                        TotalStockValueChange = request.IncludeStockData ? stockChangeData?.TotalStockValueChange : null,
                        TotalBalance = request.IncludeStockData ? d.FinancialBalance + (stockChangeData?.TotalStockValueChange ?? 0) : null
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
                StockSummary = request.IncludeStockData ? CreateStockSummary(monthlyData, stockChangesList) : null
            }
        };
        
        _logger.LogInformation("Financial overview generated with {Count} months of data, IncludeStock={IncludeStock}", 
            response.Data.Count, request.IncludeStockData);
        
        return response;
    }
    
    private static StockSummaryDto CreateStockSummary(List<MonthlyFinancialData> monthlyData, List<MonthlyStockChange> stockChanges)
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