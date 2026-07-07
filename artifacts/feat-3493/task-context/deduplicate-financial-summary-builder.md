### task: deduplicate-financial-summary-builder

**Context:** This task performs the full refactor of `backend/src/Anela.Heblo.Application/Features/FinancialOverview/Services/FinancialAnalysisService.cs` in a sequence of small, independently-compiling steps: add the new `BuildSummary` helper, restructure `GetFinancialOverviewRealTimeAsync` to materialize its DTO list once, switch all three call sites to use `BuildSummary`, then remove the now-unused `CreateStockSummary(List<MonthlyFinancialData>, List<MonthlyStockChange>)` overload. Existing tests are run before and after each meaningful edit to prove behavior is preserved — no new tests are written or existing tests modified (per spec FR-5 and Out of Scope: new direct unit tests for the private helpers are explicitly out of scope for this task).

All file paths below are relative to the repository root: `/home/user/worktrees/feature-3493-Arch-Review-Financialoverview-Financialsummarydto`.

#### Step 1: Confirm the baseline test count

Run:
```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~FinancialOverview"
```
Record the reported `Passed: N` count from the summary line. This is the number you will match after every subsequent test run in this task. Do not proceed to Step 2 until this run is green.

#### Step 2: Confirm `CreateStockSummary(` has no external callers

Run a repo-wide search to confirm the private overload being removed in Step 6 is not referenced outside this file (the architecture review already did this; this step re-verifies against the current worktree state before editing):
```bash
grep -rn "CreateStockSummary(" --include="*.cs" backend/
```
Expected: all matches are within `backend/src/Anela.Heblo.Application/Features/FinancialOverview/Services/FinancialAnalysisService.cs` (the two overload declarations at lines 504 and 520, plus the three call sites at lines 328, 386, and 495). If any match appears in another `.cs` file, stop and re-plan — this would invalidate FR-2's removal.

#### Step 3: Add the `BuildSummary` helper

Open `backend/src/Anela.Heblo.Application/Features/FinancialOverview/Services/FinancialAnalysisService.cs`. Locate the existing `CreateStockSummary(List<MonthlyFinancialDataDto> monthlyData)` method (currently at lines 504–518):

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

Insert a new `BuildSummary` method immediately **before** this `CreateStockSummary` method (i.e. right after the closing brace of `GetFinancialOverviewRealTimeAsync`, which ends at line 502, and before line 504):

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

```

Save the file. At this point `BuildSummary` exists but is not yet called anywhere — this is expected and will not cause a compiler warning (it's a private method used later in this same task, and the file won't compile-clean-with-unused-warning until later steps wire it up, which is fine since we verify build only after full wiring in Step 5).

#### Step 4: Restructure `GetFinancialOverviewRealTimeAsync` to materialize `orderedData` once

Locate `GetFinancialOverviewRealTimeAsync` (starts at line 391). Find this block near the end of the method (currently lines 477–497):

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

Note: `stockChangesList` (line 449: `var stockChangesList = stockChanges.ToList();`) and `stockChangesLookup` (line 450, built from `stockChangesList`) are both left in place — `stockChangesLookup` is still read inside the `.Select` above via `TryGetValue`. Do not remove either local in this step; `stockChangesList` becomes referenced only via `stockChangesLookup`'s construction, which is still valid.

Save the file.

#### Step 5: Switch the two remaining call sites to `BuildSummary` and build

Locate `GetHybridWithCurrentMonthAsync` (starts at line 262). Find this block near its end (currently lines 317–331):

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

Next, locate `GetCachedFinancialOverview` (starts at line 333). Find this block near its end (currently lines 375–389):

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

Save the file. Now build to confirm everything compiles with both overloads of `CreateStockSummary` still present (the unused one is removed in Step 6, kept separate so any compile error here is attributable only to the `BuildSummary` wiring, not the removal):

```bash
dotnet build backend/src/Anela.Heblo.Application/Anela.Heblo.Application.csproj
```

Expected: `Build succeeded. 0 Warning(s) 0 Error(s)` (no new warnings attributable to this file — a pre-existing unrelated warning elsewhere in the solution, if any, is not a concern here since this command builds only the Application project).

#### Step 6: Remove the unused `CreateStockSummary(List<MonthlyFinancialData>, List<MonthlyStockChange>)` overload

In `backend/src/Anela.Heblo.Application/Features/FinancialOverview/Services/FinancialAnalysisService.cs`, locate and delete this entire method (now unused after Step 4's restructuring — it was the overload's only caller):

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

Delete the whole block (method signature through closing brace, plus the blank line that follows it, so exactly one blank line separates the remaining `CreateStockSummary(List<MonthlyFinancialDataDto>)` method from `MapToDto`). Save the file.

Verify no `new FinancialSummaryDto {` object-initializer remains anywhere except inside `BuildSummary`:
```bash
grep -n "new FinancialSummaryDto" backend/src/Anela.Heblo.Application/Features/FinancialOverview/Services/FinancialAnalysisService.cs
```
Expected: exactly one match, inside `BuildSummary`.

Verify exactly one `CreateStockSummary` method remains:
```bash
grep -n "private static StockSummaryDto CreateStockSummary" backend/src/Anela.Heblo.Application/Features/FinancialOverview/Services/FinancialAnalysisService.cs
```
Expected: exactly one match, `private static StockSummaryDto CreateStockSummary(List<MonthlyFinancialDataDto> monthlyData)`.

#### Step 7: Build the full solution

```bash
dotnet build Anela.Heblo.sln
```
Expected: `Build succeeded. 0 Error(s)`, and no new warnings reported for `FinancialAnalysisService.cs` compared to the Step 5 build output (the file went from two `CreateStockSummary` overloads to one; a correct removal produces no dangling-reference errors since the deleted overload had no remaining callers per Step 2's grep).

#### Step 8: Run `dotnet format` and confirm no diff on the changed file

```bash
dotnet format Anela.Heblo.sln --verify-no-changes --include backend/src/Anela.Heblo.Application/Features/FinancialOverview/Services/FinancialAnalysisService.cs
```
Expected: exit code `0` with no output listing this file as needing formatting changes (per NFR-2, the plan's code blocks in Steps 3–6 already match the file's existing brace/indentation style). If it reports a formatting diff, run:
```bash
dotnet format Anela.Heblo.sln --include backend/src/Anela.Heblo.Application/Features/FinancialOverview/Services/FinancialAnalysisService.cs
```
then re-run the `--verify-no-changes` command above to confirm it is now clean, and re-check the file's diff (`git diff backend/src/Anela.Heblo.Application/Features/FinancialOverview/Services/FinancialAnalysisService.cs`) to make sure `dotnet format` did not alter anything beyond whitespace/brace style.

#### Step 9: Run the full FinancialOverview test suite and compare against baseline

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~FinancialOverview"
```
Expected: `Passed! - Failed: 0, Passed: N, Skipped: 0` where `N` is the exact same count recorded in the Baseline command at the top of this plan. If any test fails or the count differs, do not proceed — investigate the specific failing test (see Step 10 for the diagnostic checklist) before continuing.

Confirm no test file was modified as part of this task:
```bash
git status backend/test/Anela.Heblo.Tests/Application/FinancialOverview/
```
Expected: no output (clean — these three test files must be untouched per FR-5).

#### Step 10: Diagnostic checklist (only if Step 9 fails)

If a test fails, check these in order before making any further code change:
1. Confirm `BuildSummary`'s six aggregate expressions (Step 3) are character-for-character identical to the corresponding original inline block for the failing path (`GetHybridWithCurrentMonthAsync` used `allData`, `GetCachedFinancialOverview` used `orderedData`, `GetFinancialOverviewRealTimeAsync` used `monthlyData` pre-refactor / `orderedData` post-refactor) — a copy-paste error in `Sum`/`Average`/`.Any()` guards is the most likely cause of a numeric mismatch.
2. If the failure is specific to a real-time-path test (`GetFinancialOverviewRealTimeAsync`), confirm `orderedData` (Step 4) is being passed to *both* `Data` and `BuildSummary` — a stale reference to the old `monthlyData` domain list anywhere in `BuildSummary`'s call would produce a compile error, not a runtime mismatch, so this is unlikely but worth a visual check of the diff.
3. Run `git diff backend/src/Anela.Heblo.Application/Features/FinancialOverview/Services/FinancialAnalysisService.cs` and compare line-by-line against the exact code blocks given in Steps 3–6 of this task.

#### Step 11: Final full-solution regression check

Run the complete backend test suite once to confirm no other module was affected (this refactor should be invisible outside `FinancialOverview`, but this is a cheap final check since `CreateStockSummary` and `BuildSummary` are both `private`):

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj
```
Expected: `Passed! - Failed: 0`. Any failure outside the `FinancialOverview` namespace indicates the refactor had an unintended side effect and must be investigated before committing — though given both changed methods are `private static` helpers with no other callers (confirmed in Step 2), no such failure is expected.

#### Step 12: Commit

```bash
git add backend/src/Anela.Heblo.Application/Features/FinancialOverview/Services/FinancialAnalysisService.cs
git commit -m "Deduplicate FinancialSummaryDto construction in FinancialAnalysisService

Extract a single BuildSummary helper used by all three calculation
paths (hybrid, cached, real-time), and collapse the two
CreateStockSummary overloads into one (the DTO-list-based one).
GetFinancialOverviewRealTimeAsync now materializes its DTO list once
and reuses it for both response.Data and Summary, removing its
second independent stock-aggregation code path. Pure refactor: no
public interface, DTO shape, or behavior change; existing tests pass
unchanged."
```
Expected: commit succeeds, `git status` shows a clean working tree for this file (only the commit itself, no leftover unstaged changes).

**Task completion check:** all of the following must be true:
- Step 9's test run reports the same `Passed: N` count as the Baseline command, with `Failed: 0`.
- Step 11's full-suite run reports `Failed: 0`.
- Step 8's `dotnet format --verify-no-changes` passes.
- Step 7's `dotnet build` succeeds with no new errors or warnings.
- `grep -c "new FinancialSummaryDto" backend/src/Anela.Heblo.Application/Features/FinancialOverview/Services/FinancialAnalysisService.cs` returns `1`.
- `grep -c "private static StockSummaryDto CreateStockSummary" backend/src/Anela.Heblo.Application/Features/FinancialOverview/Services/FinancialAnalysisService.cs` returns `1`.
- `git status backend/test/Anela.Heblo.Tests/Application/FinancialOverview/` shows no changes.
