# Design: FinancialAnalysisService Coverage Gaps

## Component Design

### Test Class: FinancialAnalysisServiceTests (existing)
File: `backend/test/Anela.Heblo.Tests/Application/FinancialOverview/FinancialAnalysisServiceTests.cs`

Add two new `[Fact]` methods to the existing class:

**Test 1 — Default date range computation**
```
RefreshFinancialDataAsync_WhenDatesNull_UsesDefaultDateRange_LastDayOfPreviousMonth
```
- Sets no `financial_last_refresh` cache entry (throttle passes)
- Calls `RefreshFinancialDataAsync(null, null)` with `MonthsToCache = 3`
- Computes expected dates: `endDate = last day of previous month`, `startDate = first day of month (endDate - 2 months)`
- Verifies `_ledgerServiceMock.GetLedgerItems` is called with start date ≥ `startDate` and end date ≤ `endDate`

**Test 2 — Month-by-month loop call count**
```
RefreshFinancialDataAsync_WhenOutsideThrottleWindow_CallsLedgerOncePerMonth
```
- Sets no `financial_last_refresh` cache entry
- Calls `RefreshFinancialDataAsync(null, null)` with `MonthsToCache = 3`
- Verifies `_ledgerServiceMock.GetLedgerItems` is called exactly 6 times (2 calls × 3 months)
- Verifies `_stockValueServiceMock.GetStockValueChangesAsync` is called exactly 3 times (1 call × 3 months)

## Data Schemas

### LedgerItem (existing domain model)
```csharp
public class LedgerItem
{
    public DateTime Date { get; set; }
    public string? DebitAccountNumber { get; set; }
    public string? CreditAccountNumber { get; set; }
    public decimal Amount { get; set; }
    public string? Department { get; set; }
}
```

### Key production constants (referenced in tests)
```csharp
// Cache keys
"financial_last_refresh"         // throttle timestamp
"financial_monthly_data_{y}_{m}" // monthly data cache
"financial_stock_data_{y}_{m}"   // stock data cache

// Throttle window
TimeSpan.FromMinutes(10)

// ILedgerService.GetLedgerItems signature (both debit and credit calls)
GetLedgerItems(DateTime from, DateTime to,
    IEnumerable<string>? debitAccountPrefix,
    IEnumerable<string>? creditAccountPrefix,
    string? department,
    CancellationToken ct)
```
