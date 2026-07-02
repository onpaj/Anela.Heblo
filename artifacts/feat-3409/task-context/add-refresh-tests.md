# Task Plan: FinancialAnalysisService Coverage Gaps

### task: add-refresh-tests

## Goal
Add two unit tests to `FinancialAnalysisServiceTests.cs` that cover the untested paths in `RefreshFinancialDataAsync`:
1. **Default date range**: verify that `endDate` defaults to the last day of the previous month and `startDate` defaults to `endDate - MonthsToCache + 1` months when both parameters are null.
2. **Month-by-month loop**: verify that the ledger and stock services are each called once per month in the configured date range.

## Context

### Source file under test
`backend/src/Anela.Heblo.Application/Features/FinancialOverview/Services/FinancialAnalysisService.cs`

Key logic in `RefreshFinancialDataAsync`:
```csharp
public async Task RefreshFinancialDataAsync(DateTime? startDate, DateTime? endDate, CancellationToken ct = default)
{
    var lastRefresh = _memoryCache.Get<DateTime?>(LAST_REFRESH_CACHE_KEY) ?? DateTime.MinValue;
    if (DateTime.UtcNow - lastRefresh < TimeSpan.FromMinutes(10))
    {
        _logger.LogDebug("Skipping refresh, last refresh was too recent");
        return;
    }

    endDate ??= new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1).AddDays(-1); // Last day of previous month
    startDate ??= endDate.Value.AddMonths(-_options.MonthsToCache + 1);

    var currentDate = endDate.Value;
    while (currentDate >= startDate)
    {
        var monthStart = new DateTime(currentDate.Year, currentDate.Month, 1);
        var monthEnd = monthStart.AddMonths(1).AddDays(-1);
        await RefreshMonthlyDataAsync(monthStart, monthEnd, ct);
        currentDate = new DateTime(currentDate.Year, currentDate.Month, 1).AddMonths(-1);
    }
    _memoryCache.Set(LAST_REFRESH_CACHE_KEY, DateTime.UtcNow, TimeSpan.FromHours(24));
}
```

`RefreshMonthlyDataAsync` calls both `_ledgerService.GetLedgerItems` twice (once for debit prefix, once for credit prefix) and `_stockValueService.GetStockValueChangesAsync` once per month.

### Test file to modify
`backend/test/Anela.Heblo.Tests/Application/FinancialOverview/FinancialAnalysisServiceTests.cs`

The test class constructor sets up:
```csharp
private readonly Mock<ILedgerService> _ledgerServiceMock;
private readonly Mock<IStockValueService> _stockValueServiceMock;
private readonly IMemoryCache _memoryCache;
private readonly FinancialAnalysisService _service;
```

Default mock setups return empty lists for both services. Cache key for throttle guard: `"financial_last_refresh"`.

The `FinancialAnalysisOptions.MonthsToCache` is set to `24` in the constructor. For the new tests, create a separate service instance with `MonthsToCache = 3` to make the loop count manageable.

### ILedgerService signature
```csharp
Task<IReadOnlyList<LedgerItem>> GetLedgerItems(
    DateTime from, DateTime to,
    IEnumerable<string>? debitAccountPrefix = null,
    IEnumerable<string>? creditAccountPrefix = null,
    string? department = null,
    CancellationToken cancellationToken = default);
```

## Files to create/modify
- **Modify**: `backend/test/Anela.Heblo.Tests/Application/FinancialOverview/FinancialAnalysisServiceTests.cs`
  - Add two new `[Fact]` methods at the end of the class

## Implementation steps

### Step 1: Add test for default date range

Add this test method:
```csharp
[Fact]
public async Task RefreshFinancialDataAsync_WhenDatesNull_UsesDefaultDateRange()
{
    // Arrange — create a 3-month service so the expected dates are predictable
    var options3 = Options.Create(new FinancialAnalysisOptions { MonthsToCache = 3 });
    var service3 = new FinancialAnalysisService(
        _ledgerServiceMock.Object,
        _stockValueServiceMock.Object,
        NullLogger<FinancialAnalysisService>.Instance,
        options3,
        _memoryCache);

    // Do NOT set financial_last_refresh so the throttle passes
    var now = DateTime.UtcNow;
    var expectedEndDate = new DateTime(now.Year, now.Month, 1).AddDays(-1);
    var expectedStartDate = new DateTime(expectedEndDate.AddMonths(-2).Year, expectedEndDate.AddMonths(-2).Month, 1);

    // Act
    await service3.RefreshFinancialDataAsync(startDate: null, endDate: null);

    // Assert — ledger should be called with the computed date range
    // The while-loop first processes endDate month, so the first call starts at the first day of endDate's month
    _ledgerServiceMock.Verify(
        x => x.GetLedgerItems(
            It.Is<DateTime>(d => d >= expectedStartDate),
            It.Is<DateTime>(d => d <= expectedEndDate),
            It.IsAny<IEnumerable<string>?>(),
            It.IsAny<IEnumerable<string>?>(),
            It.IsAny<string?>(),
            It.IsAny<CancellationToken>()),
        Times.AtLeast(1),
        "ledger should be called within the computed default date range");
}
```

### Step 2: Add test for month-by-month loop call count

Add this test method:
```csharp
[Fact]
public async Task RefreshFinancialDataAsync_WhenOutsideThrottleWindow_CallsServicesOncePerMonth()
{
    // Arrange — use MonthsToCache = 3 for predictable loop count
    var cache3 = new MemoryCache(new MemoryCacheOptions());
    var options3 = Options.Create(new FinancialAnalysisOptions { MonthsToCache = 3 });
    var service3 = new FinancialAnalysisService(
        _ledgerServiceMock.Object,
        _stockValueServiceMock.Object,
        NullLogger<FinancialAnalysisService>.Instance,
        options3,
        cache3);

    // Do NOT set financial_last_refresh — cache3 is fresh

    // Act
    await service3.RefreshFinancialDataAsync(startDate: null, endDate: null);

    // Assert — each of 3 months triggers 2 ledger calls (debit + credit) and 1 stock call
    _ledgerServiceMock.Verify(
        x => x.GetLedgerItems(
            It.IsAny<DateTime>(),
            It.IsAny<DateTime>(),
            It.IsAny<IEnumerable<string>?>(),
            It.IsAny<IEnumerable<string>?>(),
            It.IsAny<string?>(),
            It.IsAny<CancellationToken>()),
        Times.Exactly(6),
        "ledger should be called twice per month (debit + credit) for 3 months = 6 calls");

    _stockValueServiceMock.Verify(
        x => x.GetStockValueChangesAsync(
            It.IsAny<DateTime>(),
            It.IsAny<DateTime>(),
            It.IsAny<CancellationToken>()),
        Times.Exactly(3),
        "stock service should be called once per month for 3 months = 3 calls");

    // Assert the refresh timestamp is cached after a successful run
    cache3.TryGetValue("financial_last_refresh", out DateTime? lastRefresh).Should().BeTrue(
        "last refresh timestamp should be cached after successful refresh");
    lastRefresh.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
}
```

## Tests to write
- `RefreshFinancialDataAsync_WhenDatesNull_UsesDefaultDateRange` — FR-1
- `RefreshFinancialDataAsync_WhenOutsideThrottleWindow_CallsServicesOncePerMonth` — FR-2

## Acceptance criteria
1. Both new tests are added to `FinancialAnalysisServiceTests.cs`
2. `dotnet test` passes with no failures in the `FinancialOverview` test folder
3. No production code is changed

## Notes
- `MonthsToCache = 24` (the class-level `_service`) is kept as-is for all existing tests — new tests create their own `service3` with `MonthsToCache = 3` to make loop assertions tractable
- The `cache3` in Test 2 is a separate `MemoryCache` instance so the throttle test (which sets `financial_last_refresh` on `_memoryCache`) doesn't interfere
