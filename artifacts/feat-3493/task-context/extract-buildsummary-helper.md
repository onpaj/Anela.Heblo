### task: extract-buildsummary-helper

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/FinancialOverview/Services/FinancialAnalysisService.cs`
- Modify: `backend/test/Anela.Heblo.Tests/Application/FinancialOverview/FinancialAnalysisServiceTests.cs`

#### Steps

1. **Add a new test helper to seed the stock cache entry (mirrors existing `SeedCacheForMonth`).**

   Open `backend/test/Anela.Heblo.Tests/Application/FinancialOverview/FinancialAnalysisServiceTests.cs`. Find the existing `SeedCacheForMonth` helper (around line 206-216):

   ```csharp
       private void SeedCacheForMonth(int year, int month)
       {
           var key = $"financial_monthly_data_{year}_{month}";
           _memoryCache.Set(key, new Anela.Heblo.Domain.Features.FinancialOverview.MonthlyFinancialData
           {
               Year = year,
               Month = month,
               Income = 10000m,
               Expenses = 8000m
           }, TimeSpan.FromHours(1));
       }
   ```

   Immediately after this method, add a new helper that seeds the matching stock-data cache entry (the service's private `STOCK_DATA_CACHE_KEY_PREFIX` constant is `"financial_stock_data_"`):

   ```csharp
       private void SeedStockCacheForMonth(int year, int month, decimal materials, decimal semiProducts, decimal products)
       {
           var key = $"financial_stock_data_{year}_{month}";
           _memoryCache.Set(key, new MonthlyStockChange
           {
               Year = year,
               Month = month,
               StockChanges = new StockChangeByType
               {
                   Materials = materials,
                   SemiProducts = semiProducts,
                   Products = products
               }
           }, TimeSpan.FromHours(1));
       }
   ```

   This will not yet compile as a standalone step (the test class doesn't use `MonthlyStockChange`/`StockChangeByType` unqualified yet — they're both in `Anela.Heblo.Domain.Features.FinancialOverview`, already `using`'d at the top of the file via `using Anela.Heblo.Domain.Features.FinancialOverview;` on line 4), so this compiles as-is. Do not run the build yet — continue to step 2 first so the new tests exist before you check compilation.

   - [ ] Add `SeedStockCacheForMonth` helper as shown above, directly after `SeedCacheForMonth`.

2. **Add the new test: real-time path with a matching stock change.**

   Add this test method anywhere among the other `[Fact]` methods (e.g., directly after `GetFinancialOverviewAsync_RealTime_ComputesIncomeAndExpensesByAccountPrefix_PreservingAllFr4Cases`, which ends at line 241):

   ```csharp
       [Fact]
       public async Task GetFinancialOverviewAsync_RealTimePath_WithMatchingStockChange_ComputesStockSummaryFromDtoValues()
       {
           // Arrange — empty cache routes to real-time path; months:1 + includeCurrentMonth:false
           // means the only month in range is last month (end date = last day of previous month).
           var now = DateTime.UtcNow;
           var lastMonthStart = new DateTime(now.Year, now.Month, 1).AddMonths(-1);

           // Ledger mock keeps the default empty-list setup from the constructor, so
           // Income = 0, Expenses = 0, FinancialBalance = 0 for the month.

           _stockValueServiceMock
               .Setup(x => x.GetStockValueChangesAsync(
                   It.IsAny<DateTime>(),
                   It.IsAny<DateTime>(),
                   It.IsAny<CancellationToken>()))
               .ReturnsAsync(new List<MonthlyStockChange>
               {
                   new MonthlyStockChange
                   {
                       Year = lastMonthStart.Year,
                       Month = lastMonthStart.Month,
                       StockChanges = new StockChangeByType { Materials = 100m, SemiProducts = 50m, Products = 25m }
                   }
               });

           // Act
           var response = await _service.GetFinancialOverviewAsync(
               months: 1,
               includeStockData: true,
               excludedDepartments: null,
               includeCurrentMonth: false);

           // Assert — TotalStockValueChange = 100 + 50 + 25 = 175; FinancialBalance = 0 (empty ledger)
           response.Summary.StockSummary.Should().NotBeNull();
           response.Summary.StockSummary!.TotalStockValueChange.Should().Be(175m);
           response.Summary.StockSummary!.AverageMonthlyStockChange.Should().Be(175m, "only one month is in range");
           response.Summary.StockSummary!.TotalBalanceWithStock.Should().Be(175m, "TotalBalance(0) + TotalStockValueChange(175)");
           response.Summary.StockSummary!.AverageMonthlyTotalBalance.Should().Be(175m, "AverageBalance(0) + AverageStockChange(175)");
       }
   ```

   - [ ] Add this test method.

3. **Add the new test: real-time path with a non-matching stock change (treated as zero).**

   Add directly after the previous test:

   ```csharp
       [Fact]
       public async Task GetFinancialOverviewAsync_RealTimePath_WithNoMatchingStockChange_TreatsStockValueAsZero()
       {
           // Arrange — stock service returns a change for a month OUTSIDE the requested range,
           // so the per-month lookup in GetFinancialOverviewRealTimeAsync finds no match for the
           // one month actually returned (last month), and TotalStockValueChange falls back to 0.
           var now = DateTime.UtcNow;
           var lastMonthStart = new DateTime(now.Year, now.Month, 1).AddMonths(-1);
           var unrelatedMonth = lastMonthStart.AddMonths(-5);

           _stockValueServiceMock
               .Setup(x => x.GetStockValueChangesAsync(
                   It.IsAny<DateTime>(),
                   It.IsAny<DateTime>(),
                   It.IsAny<CancellationToken>()))
               .ReturnsAsync(new List<MonthlyStockChange>
               {
                   new MonthlyStockChange
                   {
                       Year = unrelatedMonth.Year,
                       Month = unrelatedMonth.Month,
                       StockChanges = new StockChangeByType { Materials = 999m, SemiProducts = 999m, Products = 999m }
                   }
               });

           // Act
           var response = await _service.GetFinancialOverviewAsync(
               months: 1,
               includeStockData: true,
               excludedDepartments: null,
               includeCurrentMonth: false);

           // Assert — no stock change matches the returned month, so it contributes 0
           response.Summary.StockSummary.Should().NotBeNull();
           response.Summary.StockSummary!.TotalStockValueChange.Should().Be(0m);
           response.Summary.StockSummary!.AverageMonthlyStockChange.Should().Be(0m);
           response.Summary.StockSummary!.TotalBalanceWithStock.Should().Be(0m, "TotalBalance(0) + TotalStockValueChange(0)");
           response.Summary.StockSummary!.AverageMonthlyTotalBalance.Should().Be(0m);
       }
   ```

   - [ ] Add this test method.

4. **Add the new test: cached path with a matching stock change (parity with the real-time formula).**

   Add directly after the previous test:

   ```csharp
       [Fact]
       public async Task GetFinancialOverviewAsync_CachedPath_WithMatchingStockChange_ComputesStockSummaryFromDtoValues()
       {
           // Arrange — seed both the monthly financial data and the stock data cache entries for
           // last month, so GetFinancialOverviewAsync routes to the cached path (CachedMonthsCount > 0)
           // and months:1 selects exactly that one cached month.
           var now = DateTime.UtcNow;
           var prevMonth = now.AddMonths(-1);
           SeedCacheForMonth(prevMonth.Year, prevMonth.Month);
           SeedStockCacheForMonth(prevMonth.Year, prevMonth.Month, materials: 100m, semiProducts: 50m, products: 25m);

           // Act
           var response = await _service.GetFinancialOverviewAsync(
               months: 1,
               includeStockData: true,
               excludedDepartments: null,
               includeCurrentMonth: false);

           // Assert — FinancialBalance = Income(10000) - Expenses(8000) = 2000 (from SeedCacheForMonth);
           // TotalStockValueChange = 100 + 50 + 25 = 175 (from SeedStockCacheForMonth).
           response.Summary.StockSummary.Should().NotBeNull();
           response.Summary.StockSummary!.TotalStockValueChange.Should().Be(175m);
           response.Summary.StockSummary!.AverageMonthlyStockChange.Should().Be(175m, "only one month is in range");
           response.Summary.StockSummary!.TotalBalanceWithStock.Should().Be(2175m, "TotalBalance(2000) + TotalStockValueChange(175)");
           response.Summary.StockSummary!.AverageMonthlyTotalBalance.Should().Be(2175m, "AverageBalance(2000) + AverageStockChange(175)");
       }
   ```

   - [ ] Add this test method.

5. **Add the new test: `includeStockData: false` keeps `StockSummary` null on the real-time path.**

   Add directly after the previous test:

   ```csharp
       [Fact]
       public async Task GetFinancialOverviewAsync_RealTimePath_IncludeStockDataFalse_StockSummaryIsNull()
       {
           // Arrange — empty cache routes to real-time path; stock service default (empty list) applies.

           // Act
           var response = await _service.GetFinancialOverviewAsync(
               months: 1,
               includeStockData: false,
               excludedDepartments: null,
               includeCurrentMonth: false);

           // Assert
           response.Summary.StockSummary.Should().BeNull();
       }
   ```

   - [ ] Add this test method.

6. **Add the new test: `includeStockData: false` keeps `StockSummary` null on the cached path.**

   Add directly after the previous test:

   ```csharp
       [Fact]
       public async Task GetFinancialOverviewAsync_CachedPath_IncludeStockDataFalse_StockSummaryIsNull()
       {
           // Arrange — seed monthly cache so the cached path is used (CachedMonthsCount > 0).
           var now = DateTime.UtcNow;
           var prevMonth = now.AddMonths(-1);
           SeedCacheForMonth(prevMonth.Year, prevMonth.Month);

           // Act
           var response = await _service.GetFinancialOverviewAsync(
               months: 1,
               includeStockData: false,
               excludedDepartments: null,
               includeCurrentMonth: false);

           // Assert
           response.Summary.StockSummary.Should().BeNull();
       }
   ```

   - [ ] Add this test method.

7. **Add the new test: zero months of data produces an empty, zero-valued summary (no divide-by-empty-sequence exception).**

   Add directly after the previous test:

   ```csharp
       [Fact]
       public async Task GetFinancialOverviewAsync_ZeroMonthsRequested_ReturnsEmptySummaryWithZeroedAverages()
       {
           // Arrange — months: 0 makes the real-time path's computed startDate land after endDate
           // (startDate = end-of-previous-month.AddMonths(1), first-of-month), so the month-by-month
           // loop never executes and monthlyData/orderedData stay empty. Cache is empty by default,
           // so this routes to the real-time path.

           // Act
           var response = await _service.GetFinancialOverviewAsync(
               months: 0,
               includeStockData: true,
               excludedDepartments: null,
               includeCurrentMonth: false);

           // Assert — data is empty; all totals are 0; averages are 0 (not NaN/exception) because
           // BuildSummary/CreateStockSummary guard every Average() call with data.Any().
           response.Data.Should().BeEmpty();
           response.Summary.TotalIncome.Should().Be(0m);
           response.Summary.AverageMonthlyIncome.Should().Be(0m);
           response.Summary.AverageMonthlyBalance.Should().Be(0m);
           response.Summary.StockSummary.Should().NotBeNull("includeStockData is true, even with zero months");
           response.Summary.StockSummary!.TotalStockValueChange.Should().Be(0m);
           response.Summary.StockSummary!.AverageMonthlyStockChange.Should().Be(0m);
           response.Summary.StockSummary!.TotalBalanceWithStock.Should().Be(0m);
           response.Summary.StockSummary!.AverageMonthlyTotalBalance.Should().Be(0m);
       }
   ```

   - [ ] Add this test method.

8. **Run the new tests against the current (pre-refactor) code to confirm they encode existing behavior correctly.**

   From the repo root:

   ```bash
   cd /home/user/worktrees/feature-3493-Arch-Review-Financialoverview-Financialsummarydto/backend/test/Anela.Heblo.Tests
   dotnet test --filter "FullyQualifiedName~FinancialAnalysisServiceTests"
   ```

   Expected: all tests pass (the 8 pre-existing tests plus the 6 new ones from steps 2-7 — 14 total), confirming the new tests are valid against the current two-overload implementation before any refactor code changes are made.

   - [ ] Run the command above.
   - [ ] Confirm all 14 tests pass (0 failed).

9. **Commit the test additions.**

   ```bash
   cd /home/user/worktrees/feature-3493-Arch-Review-Financialoverview-Financialsummarydto
   git add backend/test/Anela.Heblo.Tests/Application/FinancialOverview/FinancialAnalysisServiceTests.cs
   git commit -m "Add StockSummary coverage tests for FinancialAnalysisService (pre-refactor baseline)"
   ```

   - [ ] Commit.

10. **Add the `BuildSummary` helper.**

    Open `backend/src/Anela.Heblo.Application/Features/FinancialOverview/Services/FinancialAnalysisService.cs`. Find the sole remaining `CreateStockSummary(List<MonthlyFinancialDataDto> monthlyData)` overload (lines 504-518):

    ```csharp
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
    ```

    Insert the new `BuildSummary` helper directly **above** this method:

    ```csharp
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
    ```

    - [ ] Insert `BuildSummary` above `CreateStockSummary`.

11. **Delete the second `CreateStockSummary` overload.**

    Immediately below the (unchanged) `CreateStockSummary(List<MonthlyFinancialDataDto>)` method, delete this entire block (originally lines 520-536):

    ```csharp
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
    ```

    - [ ] Delete this block entirely.

12. **Replace the inline `FinancialSummaryDto` construction in `GetHybridWithCurrentMonthAsync`.**

    Find this block (originally lines 317-330):

    ```csharp
            return new GetFinancialOverviewResponse
            {
                Data = allData,
                Summary = new FinancialSummaryDto
                {
                    TotalIncome = allData.Sum(d => d.Income),
                    TotalExpenses = allData.Sum(d => d.Expenses),
                    TotalBalance = allData.Sum(d => d.FinancialBalance),
                    AverageMonthlyIncome = allData.Any() ? allData.Average(d => d.Income) : 0,
                    AverageMonthlyExpenses = allData.Any() ? allData.Average(d => d.Expenses) : 0,
                    AverageMonthlyBalance = allData.Any() ? allData.Average(d => d.FinancialBalance) : 0,
                    StockSummary = includeStockData ? CreateStockSummary(allData) : null
                }
            };
    ```

    Replace it with:

    ```csharp
            return new GetFinancialOverviewResponse
            {
                Data = allData,
                Summary = BuildSummary(allData, includeStockData)
            };
    ```

    - [ ] Replace this block in `GetHybridWithCurrentMonthAsync`.

13. **Replace the inline `FinancialSummaryDto` construction in `GetCachedFinancialOverview`.**

    Find this block (originally lines 375-388):

    ```csharp
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
    ```

    Replace it with:

    ```csharp
            return new GetFinancialOverviewResponse
            {
                Data = orderedData,
                Summary = BuildSummary(orderedData, includeStockData)
            };
    ```

    - [ ] Replace this block in `GetCachedFinancialOverview`.

14. **Restructure `GetFinancialOverviewRealTimeAsync` to materialize its DTO list once.**

    Find this block (originally lines 477-497):

    ```csharp
            var response = new GetFinancialOverviewResponse
            {
                Data = monthlyData.OrderByDescending(d => d.Year).ThenByDescending(d => d.Month)
                    .Select(d =>
                    {
                        var stockChangeData = stockChangesLookup.TryGetValue(new { d.Year, d.Month }, out var stockChange)
                            ? stockChange
                            : null;
                        return MapToDto(d.Year, d.Month, d.Income, d.Expenses, stockChangeData, includeStockData);
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
    ```

    Replace it with:

    ```csharp
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
    ```

    - [ ] Replace this block in `GetFinancialOverviewRealTimeAsync`.

15. **Build the backend.**

    ```bash
    cd /home/user/worktrees/feature-3493-Arch-Review-Financialoverview-Financialsummarydto/backend
    dotnet build
    ```

    Expected: `Build succeeded.` with 0 errors. If the build fails with an unused-variable warning/error about `stockChangesList` no longer being referenced, note that `stockChangesList` is still used two lines above to build `stockChangesLookup` (`var stockChangesLookup = stockChangesList.ToDictionary(...)`), so it remains referenced — do not remove it.

    - [ ] Run the command above.
    - [ ] Confirm build succeeds with no new errors or warnings.

16. **Run the full `FinancialAnalysisServiceTests` suite to confirm no regressions.**

    ```bash
    cd /home/user/worktrees/feature-3493-Arch-Review-Financialoverview-Financialsummarydto/backend/test/Anela.Heblo.Tests
    dotnet test --filter "FullyQualifiedName~FinancialAnalysisServiceTests"
    ```

    Expected: all 14 tests pass (8 pre-existing + 6 added in steps 2-7), unmodified and green, matching FR-3's "all existing tests pass unmodified" requirement plus the new StockSummary coverage.

    - [ ] Run the command above.
    - [ ] Confirm all 14 tests pass (0 failed).

17. **Run the full backend test suite to catch any unrelated regression.**

    ```bash
    cd /home/user/worktrees/feature-3493-Arch-Review-Financialoverview-Financialsummarydto/backend
    dotnet test
    ```

    Expected: all tests pass (no failures anywhere in the solution — this is a private-method refactor confined to one file, so no other test should be affected).

    - [ ] Run the command above.
    - [ ] Confirm the full suite is green.

18. **Verify formatting.**

    ```bash
    cd /home/user/worktrees/feature-3493-Arch-Review-Financialoverview-Financialsummarydto/backend
    dotnet format --verify-no-changes
    ```

    Expected: exit code 0, no output listing files that would be reformatted. If it reports formatting differences, run `dotnet format` (without `--verify-no-changes`) to apply them, review the diff to confirm it only touches whitespace/style in the file you edited, then re-run `dotnet format --verify-no-changes` to confirm it now passes.

    - [ ] Run the command above.
    - [ ] Confirm no formatting differences remain.

19. **Commit the refactor.**

    ```bash
    cd /home/user/worktrees/feature-3493-Arch-Review-Financialoverview-Financialsummarydto
    git add backend/src/Anela.Heblo.Application/Features/FinancialOverview/Services/FinancialAnalysisService.cs
    git commit -m "Extract BuildSummary helper and unify CreateStockSummary in FinancialAnalysisService"
    ```

    - [ ] Commit.

---

## Self-Review

**1. Spec coverage:**
- FR-1 (extract `BuildSummary`, replace all three inline blocks) → step 10 (add helper), steps 12-14 (replace all three call sites). Covered.
- FR-1 acceptance "only one place constructs `new FinancialSummaryDto`" → after steps 12-14, the only `new FinancialSummaryDto { ... }` in the file is inside `BuildSummary` itself (step 10). Covered.
- FR-2 (unify `CreateStockSummary`, delete two-arg overload, materialize real-time DTO list) → step 11 (delete overload), step 14 (materialize `orderedData` before building the response). Covered.
- FR-2 acceptance criteria (zero months / no matching stock change / matching stock change / `includeStockData:false`) → steps 2-7 add exactly these four scenarios as tests (matching: step 2 & 4; no match: step 3; false: steps 5 & 6; zero months: step 7). Covered.
- FR-3 (no behavioral change, existing tests pass unmodified, public signatures unchanged) → step 8 runs the new tests pre-refactor to establish baseline; step 16 re-runs everything post-refactor; no public method signature is touched anywhere in steps 10-14. Covered.
- NFR-1 (no new I/O/cache calls, same O(n) cost) → step 14's materialization replaces an inline projection with an equivalent `.ToList()` assignment; no new loop or service call introduced. Covered by construction, verified indirectly by step 17 (full test suite still green, mock call-count assertions like `RefreshFinancialDataAsync_WhenOutsideThrottleWindow_CallsServicesOncePerMonth` still pass unmodified).
- NFR-2 (security, N/A) — no action needed, nothing to cover.
- "Validation before completion" (dotnet build, dotnet format, tests) → steps 15, 16, 17, 18.

**2. Placeholder scan:** No "TBD"/"implement later"/"add appropriate error handling" phrases anywhere in the task steps above. Every code-bearing step (2-7, 10-14) shows the complete before/after C# verbatim, not a description of it. No step says "similar to Task N" — each test method and each replaced block is fully written out independently since an engineer may read steps out of order.

**3. Type consistency:** `BuildSummary(List<MonthlyFinancialDataDto> data, bool includeStockData)` returns `FinancialSummaryDto` and is called identically at all three sites (`BuildSummary(allData, includeStockData)`, `BuildSummary(orderedData, includeStockData)`, `BuildSummary(orderedData, includeStockData)`) — parameter and return types match the spec/design/arch-review verbatim. `CreateStockSummary(List<MonthlyFinancialDataDto> monthlyData)` signature is unchanged from the pre-existing overload being kept. Test helper `SeedStockCacheForMonth` constructs `MonthlyStockChange { Year, Month, StockChanges = new StockChangeByType { Materials, SemiProducts, Products } }`, matching the real type definitions in `backend/src/Anela.Heblo.Domain/Features/FinancialOverview/MonthlyStockChange.cs`. All new test assertions reference `response.Summary.StockSummary!.{TotalStockValueChange, AverageMonthlyStockChange, TotalBalanceWithStock, AverageMonthlyTotalBalance}`, matching `StockSummaryDto`'s actual four properties in `backend/src/Anela.Heblo.Application/Features/FinancialOverview/Model/StockSummaryDto.cs`. No gaps found.
