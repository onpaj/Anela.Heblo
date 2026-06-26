# Extract Duplicated Period Totals Calculation in FinancialAnalysisService — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Eliminate three copy-pasted blocks of income/expenses computation in `FinancialAnalysisService` by extracting a single private static helper, with zero observable behavior change.

**Architecture:** Pure in-class refactor — add one `private static` helper to `FinancialAnalysisService.cs`, replace the three duplicated blocks with calls to it, and lock the behavior in with a public-surface characterization test exercising the six FR-4 ledger-item cases. No new files, no new types, no new namespaces, no DI changes, no contract changes.

**Tech Stack:** .NET 8 / C# 12, xUnit, FluentAssertions, Moq, MemoryCache. All changes scoped to `backend/`.

---

## File Structure

```
backend/src/Anela.Heblo.Application/Features/FinancialOverview/Services/
└── FinancialAnalysisService.cs                ← MODIFY: add private static helper; replace 3 blocks

backend/test/Anela.Heblo.Tests/Application/FinancialOverview/
└── FinancialAnalysisServiceTests.cs           ← MODIFY: add one [Fact] covering FR-4 cases
```

No new files. No new types.

---

## Critical Implementation Notes

**These are the three call sites being replaced** (verified against current source on this branch):

| Site | Method | Source lines | Input locals to pass into helper |
|------|--------|--------------|----------------------------------|
| 1 | `RefreshMonthlyDataAsync` | 219–233 | `debitItems`, `creditItems` |
| 2 | `GetHybridWithCurrentMonthAsync` | 303–309 | `debitItems`, `creditItems` |
| 3 | `GetFinancialOverviewRealTimeAsync` | 506–521 | **`monthDebitItems`, `monthCreditItems`** (per-month filtered enumerables, **not** the outer `debitItems`/`creditItems`) |

> **CRITICAL — Site 3 mismatch:** At Site 3, the loop iterates months and filters per-month items into `monthDebitItems` / `monthCreditItems` (lines 502–503). The helper call here MUST use these per-month locals. Using the outer `debitItems` / `creditItems` would aggregate across all months and silently corrupt the report. This is the only literal deviation from the brief's snippet — the architecture review flagged it as the highest-severity risk in the refactor.

**Predicate preservation (FR-1, FR-3):** The four LINQ predicates inside the helper MUST be byte-for-byte identical to today's predicates:
- `item.DebitAccountNumber?.StartsWith("5") == true`
- `item.CreditAccountNumber?.StartsWith("5") == true`
- `item.CreditAccountNumber?.StartsWith("6") == true`
- `item.DebitAccountNumber?.StartsWith("6") == true`

Do NOT "improve" them by switching to pattern matching, hoisting `"5"`/`"6"` to constants, or rewriting the null guards. These changes are explicitly out of scope (NFR-4) and would risk altering behavior on null account numbers.

**Enumeration replayability (NFR-1):** The helper enumerates each input collection twice (once for the `"5"` filter, once for the `"6"` filter), preserving today's behavior. Do NOT add `.ToList()` inside the helper or at any call site. Today's call sites already pass replayable enumerables (materialized lists or `.Where(...)` over a list).

---

## Task 1: Add characterization test capturing the FR-4 ledger-item cases

**Goal:** Lock in the current behavior of the calculation through the public surface so any unintended drift during the refactor is caught immediately. This test MUST pass against the current (pre-refactor) code.

**Files:**
- Modify: `backend/test/Anela.Heblo.Tests/Application/FinancialOverview/FinancialAnalysisServiceTests.cs`

- [ ] **Step 1.1: Open the existing test file and locate the end of the `FinancialAnalysisServiceTests` class**

Read `backend/test/Anela.Heblo.Tests/Application/FinancialOverview/FinancialAnalysisServiceTests.cs`. The class uses xUnit + FluentAssertions + Moq, and the constructor already wires up `_ledgerServiceMock`, `_stockValueServiceMock`, `_memoryCache`, and `_service`. The default ledger mock returns an empty list for any call. Add the new test as a new `[Fact]` method inside the class.

- [ ] **Step 1.2: Add the failing-then-passing characterization test**

Add the following `[Fact]` to the end of `FinancialAnalysisServiceTests` (just before the closing brace of the class):

```csharp
    [Fact]
    public async Task GetFinancialOverviewAsync_RealTime_ComputesIncomeAndExpensesByAccountPrefix_PreservingAllFr4Cases()
    {
        // Arrange — pick the last completed month (the real-time path with includeCurrentMonth=false
        // produces a window that ends on the last day of the previous month).
        var now = DateTime.UtcNow;
        var lastMonthStart = new DateTime(now.Year, now.Month, 1).AddMonths(-1);
        var midLastMonth = lastMonthStart.AddDays(10);

        // Debit-side items: the helper sums those whose DebitAccountNumber starts with "5"
        // (positive contribution to expenses) or "6" (negative contribution to income).
        var debitItems = new List<LedgerItem>
        {
            // Case: debit-5 → adds to expenses
            new LedgerItem { Date = midLastMonth, DebitAccountNumber = "501100", CreditAccountNumber = null!, Amount = 100m },
            // Case: debit-6 → subtracts from income
            new LedgerItem { Date = midLastMonth, DebitAccountNumber = "601200", CreditAccountNumber = null!, Amount = 50m },
            // Case: null DebitAccountNumber → must be ignored, must not throw
            new LedgerItem { Date = midLastMonth, DebitAccountNumber = null!, CreditAccountNumber = null!, Amount = 1000m },
            // Case: prefix other than "5"/"6" → must be ignored
            new LedgerItem { Date = midLastMonth, DebitAccountNumber = "311000", CreditAccountNumber = null!, Amount = 999m },
        };

        // Credit-side items: the helper sums those whose CreditAccountNumber starts with "5"
        // (negative contribution to expenses) or "6" (positive contribution to income).
        var creditItems = new List<LedgerItem>
        {
            // Case: credit-5 → subtracts from expenses
            new LedgerItem { Date = midLastMonth, DebitAccountNumber = null!, CreditAccountNumber = "501100", Amount = 20m },
            // Case: credit-6 → adds to income
            new LedgerItem { Date = midLastMonth, DebitAccountNumber = null!, CreditAccountNumber = "601200", Amount = 200m },
            // Case: null CreditAccountNumber → must be ignored, must not throw
            new LedgerItem { Date = midLastMonth, DebitAccountNumber = null!, CreditAccountNumber = null!, Amount = 1000m },
            // Case: prefix other than "5"/"6" → must be ignored
            new LedgerItem { Date = midLastMonth, DebitAccountNumber = null!, CreditAccountNumber = "311000", Amount = 999m },
        };

        // Override the default empty-list setup. The service calls ILedgerService twice:
        // once with debitAccountPrefix != null (creditAccountPrefix == null), and
        // once with creditAccountPrefix != null (debitAccountPrefix == null).
        _ledgerServiceMock
            .Setup(x => x.GetLedgerItems(
                It.IsAny<DateTime>(),
                It.IsAny<DateTime>(),
                It.Is<IEnumerable<string>?>(p => p != null),
                It.Is<IEnumerable<string>?>(p => p == null),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(debitItems);

        _ledgerServiceMock
            .Setup(x => x.GetLedgerItems(
                It.IsAny<DateTime>(),
                It.IsAny<DateTime>(),
                It.Is<IEnumerable<string>?>(p => p == null),
                It.Is<IEnumerable<string>?>(p => p != null),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(creditItems);

        // Act — route through GetFinancialOverviewRealTimeAsync. With excludedDepartments=null,
        // includeCurrentMonth=false, and an empty cache (fresh test instance), the public method
        // falls through to the real-time path and applies the helper per month.
        var response = await _service.GetFinancialOverviewAsync(
            months: 2,
            includeStockData: false,
            excludedDepartments: null,
            includeCurrentMonth: false);

        // Assert — the last completed month must contain the expected income/expenses.
        // expenses = debit5(100) - credit5(20) = 80
        // income   = credit6(200) - debit6(50) = 150
        var lastMonthData = response.Data
            .FirstOrDefault(d => d.Year == lastMonthStart.Year && d.Month == lastMonthStart.Month);

        lastMonthData.Should().NotBeNull("the response must include the last completed month");
        lastMonthData!.Expenses.Should().Be(80m, "expenses = debit5(100) - credit5(20)");
        lastMonthData!.Income.Should().Be(150m, "income = credit6(200) - debit6(50)");
    }
```

- [ ] **Step 1.3: Run the new test to verify it PASSES against current (pre-refactor) code**

Run:

```bash
cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~GetFinancialOverviewAsync_RealTime_ComputesIncomeAndExpensesByAccountPrefix_PreservingAllFr4Cases" \
  --nologo
```

Expected: **1 passed**. The test exercises today's duplicated block in `GetFinancialOverviewRealTimeAsync` (lines 506–521) and asserts the exact arithmetic. If it fails now, the test fixture is wrong — fix the test before continuing (do NOT touch production code).

- [ ] **Step 1.4: Run the entire `FinancialAnalysisServiceTests` class to confirm no regressions**

Run:

```bash
cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~FinancialAnalysisServiceTests" \
  --nologo
```

Expected: **all tests pass** (the new one plus every pre-existing test in the class).

- [ ] **Step 1.5: Commit**

```bash
git add backend/test/Anela.Heblo.Tests/Application/FinancialOverview/FinancialAnalysisServiceTests.cs
git commit -m "test: characterize period totals calculation in FinancialAnalysisService

Add public-surface test covering all FR-4 ledger-item cases (debit-5,
credit-5, credit-6, debit-6, null account numbers, irrelevant prefixes)
through GetFinancialOverviewRealTimeAsync. Locks in current behavior
before the upcoming helper extraction."
```

---

## Task 2: Add `CalculatePeriodTotals` helper and replace Site 1 (`RefreshMonthlyDataAsync`)

**Goal:** Introduce the single private static helper and wire up the first call site. These two changes ship together so the new helper does not become an unused private member (which would compile-warn).

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/FinancialOverview/Services/FinancialAnalysisService.cs:219-233` (replace duplicated block)
- Modify: same file, add new private static method below the constructor (see Step 2.1)

- [ ] **Step 2.1: Add the `CalculatePeriodTotals` private static helper**

Open `backend/src/Anela.Heblo.Application/Features/FinancialOverview/Services/FinancialAnalysisService.cs`. Locate the constructor (lines 24–36). Immediately after the closing brace of the constructor and before `public async Task<GetFinancialOverviewResponse> GetFinancialOverviewAsync(` (line 38), insert this method:

```csharp
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
```

The file already has `using System.Linq;` (implicit via global usings) and `using Anela.Heblo.Domain.Accounting.Ledger;` at line 2 — no new `using` directives required.

- [ ] **Step 2.2: Replace the duplicated block at Site 1 (`RefreshMonthlyDataAsync`, lines 219–233)**

In the same file, locate `RefreshMonthlyDataAsync` and find this block:

```csharp
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
```

Replace it with:

```csharp
            // Calculate financial data for this month
            var (income, expenses) = CalculatePeriodTotals(debitItems, creditItems);
```

Downstream code (the `MonthlyFinancialData` constructor at lines 236–242 and the log statement at lines 256–257) already references `income` and `expenses` by name — no other changes needed in this method.

- [ ] **Step 2.3: Build to verify the helper compiles and is consumed**

Run:

```bash
cd backend && dotnet build src/Anela.Heblo.Application/Anela.Heblo.Application.csproj --nologo
```

Expected: **Build succeeded**, zero new warnings. (No CS0414 "private member is unused" because `RefreshMonthlyDataAsync` now calls it.)

- [ ] **Step 2.4: Run the characterization test plus the full `FinancialAnalysisServiceTests` class**

Run:

```bash
cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~FinancialAnalysisServiceTests" \
  --nologo
```

Expected: **all tests pass**. Site 1's behavior is unchanged because only `RefreshMonthlyDataAsync` was modified, and its inputs (`debitItems`, `creditItems` at lines 214–215) match the helper's expected inputs exactly.

- [ ] **Step 2.5: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/FinancialOverview/Services/FinancialAnalysisService.cs
git commit -m "refactor: extract CalculatePeriodTotals helper; replace RefreshMonthlyDataAsync block

Introduces a private static helper that centralizes the 5/6 account-prefix
arithmetic and replaces the first of three duplicated blocks. Behavior
preserved — predicates and arithmetic are byte-identical to the original."
```

---

## Task 3: Replace Site 2 (`GetHybridWithCurrentMonthAsync`)

**Goal:** Replace the second duplicated block. Inputs match the helper's signature exactly — locals are already named `debitItems` and `creditItems`.

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/FinancialOverview/Services/FinancialAnalysisService.cs:303-309`

- [ ] **Step 3.1: Replace the duplicated block at Site 2**

Locate `GetHybridWithCurrentMonthAsync` in the same file. Find this block (lines 302–309 in current source):

```csharp
        // Compute income/expenses for current month
        var debit5 = debitItems.Where(i => i.DebitAccountNumber?.StartsWith("5") == true).Sum(i => i.Amount);
        var credit5 = creditItems.Where(i => i.CreditAccountNumber?.StartsWith("5") == true).Sum(i => i.Amount);
        var expenses = debit5 - credit5;

        var credit6 = creditItems.Where(i => i.CreditAccountNumber?.StartsWith("6") == true).Sum(i => i.Amount);
        var debit6 = debitItems.Where(i => i.DebitAccountNumber?.StartsWith("6") == true).Sum(i => i.Amount);
        var income = credit6 - debit6;
```

Replace it with:

```csharp
        // Compute income/expenses for current month
        var (income, expenses) = CalculatePeriodTotals(debitItems, creditItems);
```

Leave the subsequent line `var financialBalance = income - expenses;` (currently at line 310) untouched — it references `income` and `expenses` which are still in scope from the destructured tuple.

- [ ] **Step 3.2: Build**

Run:

```bash
cd backend && dotnet build src/Anela.Heblo.Application/Anela.Heblo.Application.csproj --nologo
```

Expected: **Build succeeded**, zero new warnings.

- [ ] **Step 3.3: Run the `FinancialAnalysisServiceTests` class**

Run:

```bash
cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~FinancialAnalysisServiceTests" \
  --nologo
```

Expected: **all tests pass**. The hybrid path tests (e.g. `GetFinancialOverviewAsync_WhenIncludeCurrentMonthTrue_AndCachePopulated_OnlyFetchesCurrentMonthFromLedger`) cover this code path through the public surface.

- [ ] **Step 3.4: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/FinancialOverview/Services/FinancialAnalysisService.cs
git commit -m "refactor: route GetHybridWithCurrentMonthAsync through CalculatePeriodTotals

Replaces the second duplicated 5/6-prefix calculation block with the
shared helper. Behavior preserved — financialBalance and downstream DTO
construction unchanged."
```

---

## Task 4: Replace Site 3 (`GetFinancialOverviewRealTimeAsync`) — uses per-month locals

**Goal:** Replace the third and final duplicated block. **CRITICAL:** this site uses `monthDebitItems` / `monthCreditItems` (per-month filtered enumerables, lines 502–503), NOT the outer `debitItems` / `creditItems` defined earlier in the method (lines 481–487).

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/FinancialOverview/Services/FinancialAnalysisService.cs:505-521`

- [ ] **Step 4.1: Replace the duplicated block at Site 3 — using the per-month locals**

Locate `GetFinancialOverviewRealTimeAsync` in the same file. Inside the `while (currentDate <= endDate)` loop, find this block (lines 505–521 in current source):

```csharp
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
```

Replace it with:

```csharp
            var (income, expenses) = CalculatePeriodTotals(monthDebitItems, monthCreditItems);
```

> **VERIFY before committing:** the arguments passed are `monthDebitItems`, `monthCreditItems` (defined on lines 502–503 inside this loop), NOT the outer `debitItems`, `creditItems` (defined on lines 481–487 above the loop). Using the outer locals would aggregate all months into every bucket and silently corrupt the report. Re-read your diff to confirm before continuing.

The `monthlyData.Add(new MonthlyFinancialData { ... Income = income, Expenses = expenses })` block (lines 523–529) is unchanged — it consumes `income` and `expenses` from the destructured tuple.

- [ ] **Step 4.2: Build**

Run:

```bash
cd backend && dotnet build src/Anela.Heblo.Application/Anela.Heblo.Application.csproj --nologo
```

Expected: **Build succeeded**, zero new warnings.

- [ ] **Step 4.3: Run the `FinancialAnalysisServiceTests` class — the characterization test is the regression guard for Site 3**

Run:

```bash
cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~FinancialAnalysisServiceTests" \
  --nologo
```

Expected: **all tests pass**, including `GetFinancialOverviewAsync_RealTime_ComputesIncomeAndExpensesByAccountPrefix_PreservingAllFr4Cases` (added in Task 1). If this test fails now, the most likely cause is passing the wrong locals at Step 4.1 — re-read the call site and verify `monthDebitItems` / `monthCreditItems` are used.

- [ ] **Step 4.4: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/FinancialOverview/Services/FinancialAnalysisService.cs
git commit -m "refactor: route GetFinancialOverviewRealTimeAsync through CalculatePeriodTotals

Replaces the last of three duplicated 5/6-prefix blocks. Per-month
bucketing preserved by passing the loop-local monthDebitItems and
monthCreditItems (NOT the outer debitItems/creditItems) into the helper."
```

---

## Task 5: Full validation gates

**Goal:** Confirm the whole repository still builds, formats, and tests cleanly before declaring the refactor done.

**Files:** None modified (validation only).

- [ ] **Step 5.1: Full backend build**

Run:

```bash
cd backend && dotnet build --nologo
```

Expected: **Build succeeded**, zero errors, zero new warnings. (NFR FR-5: "no new warnings introduced by the change".)

- [ ] **Step 5.2: Format check**

Run:

```bash
cd backend && dotnet format --verify-no-changes --no-restore
```

Expected: **exit code 0**, no formatting differences reported. If this fails, run `dotnet format` (without `--verify-no-changes`) to apply fixes and inspect the diff — only re-stage and amend if the changes are limited to the lines this refactor touched. If `dotnet format` rewrites unrelated code, revert those changes (project guideline: surgical changes only).

- [ ] **Step 5.3: Full backend test suite**

Run:

```bash
cd backend && dotnet test --nologo
```

Expected: **all tests pass** with no regressions in any project. The characterization test added in Task 1 is included.

- [ ] **Step 5.4: Confirm no frontend or generated-client changes were produced**

Run:

```bash
git status --short
```

Expected output: **clean working tree** (all four commits from Tasks 1–4 are already in history). If `frontend/` or any generated TypeScript file appears modified, investigate — this refactor must not touch those paths.

- [ ] **Step 5.5: Quick visual sanity check of the final source**

Open `backend/src/Anela.Heblo.Application/Features/FinancialOverview/Services/FinancialAnalysisService.cs` and confirm:

- Exactly one method named `CalculatePeriodTotals` exists.
- The string literals `"5"` and `"6"` appear only inside `CalculatePeriodTotals` (search the file — they should not appear in any other body of the three refactored methods). The `new[] { "5", "6" }` array literals passed to `GetLedgerItems` at lines ~199, ~206, ~283, ~289, ~456, ~462 are unrelated (ledger fetch prefix filter) and must remain.
- The locals `debit5`, `credit5`, `debit6`, `credit6` no longer appear anywhere in `RefreshMonthlyDataAsync`, `GetHybridWithCurrentMonthAsync`, or `GetFinancialOverviewRealTimeAsync`.

If anything looks off, fix it as a fresh commit — do NOT amend the historical commits.

- [ ] **Step 5.6: Final commit (only if Step 5.5 surfaced a touch-up)**

If Step 5.5 required a fix, commit it:

```bash
git add backend/src/Anela.Heblo.Application/Features/FinancialOverview/Services/FinancialAnalysisService.cs
git commit -m "refactor: clean up residual duplication after CalculatePeriodTotals extraction"
```

Otherwise skip this step.

---

## Spec Coverage Self-Review

| Spec requirement | Implemented in |
|---|---|
| FR-1: Single shared `CalculatePeriodTotals` helper (private static, exact predicates, value-tuple return) | Task 2, Step 2.1 |
| FR-2: Three duplicated blocks replaced with single-line destructuring calls | Task 2 Step 2.2 (Site 1), Task 3 Step 3.1 (Site 2), Task 4 Step 4.1 (Site 3) |
| FR-3: Behavior preservation (identical numeric output, no new public surface, no DI/signature/log changes) | Predicate-preservation note above all tasks; characterization test (Task 1) is the regression guard; no edits permitted to ctor, DTOs, logs, exceptions |
| FR-4: Test coverage exercising all six ledger-item cases (debit-5, credit-5, credit-6, debit-6, null account numbers, irrelevant prefixes) via public surface | Task 1, Step 1.2 |
| FR-5: Validation gates (`dotnet build`, `dotnet format`, full backend test suite, no frontend/generated changes) | Task 5 |
| NFR-1: No regression in enumeration count, no `.ToList()` added inside helper or at call sites | Helper definition in Step 2.1 + "Enumeration replayability" note above tasks |
| NFR-3 / NFR-4: Single change-point established; magic literals stay in helper; no constant extraction | Out-of-scope per spec; tasks do not touch unrelated code |
| Arch-review Amendment 1: Site 3 uses `monthDebitItems` / `monthCreditItems` | Task 4, Step 4.1 (called out as CRITICAL) |
| Arch-review Amendment 2: Document enumeration-replayability assumption | "Critical Implementation Notes" section above tasks |
| Arch-review Amendment 3: Test routes through `GetFinancialOverviewRealTimeAsync` with `includeCurrentMonth=false` | Task 1, Step 1.2 (the Act block uses exactly these parameters) |
| Out of scope: no helper relocation, no constant extraction, no method splits, no `LedgerItem` changes, no frontend changes | Plan tasks do not touch these areas; Step 5.4 confirms |

No gaps. No placeholders. Method signature `(decimal income, decimal expenses) CalculatePeriodTotals(IEnumerable<LedgerItem>, IEnumerable<LedgerItem>)` is used identically in Tasks 2, 3, and 4.
