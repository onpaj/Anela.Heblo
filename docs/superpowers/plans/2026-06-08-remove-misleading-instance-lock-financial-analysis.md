# Remove misleading instance-level lock in FinancialAnalysisService — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Delete the decorative `_refreshLock` instance lock from `FinancialAnalysisService` and rely solely on the shared `IMemoryCache` timestamp throttle, so the code accurately reflects its true concurrency guarantees.

**Architecture:** `FinancialAnalysisService` is registered Scoped — every HTTP request and every `IServiceProvider.CreateScope()` (used by `BackgroundRefreshSchedulerService`) gets a fresh service instance with its own `_refreshLock`. The lock therefore guards only the single request thread that already owns the instance and provides zero cross-request protection. The only real cross-request gate is the `IMemoryCache` entry at key `financial_last_refresh`, which `IMemoryCache` makes thread-safe on its own. This plan removes the misleading lock and adds one regression test that pins the throttle behavior in place.

**Tech Stack:** .NET 8, C#, `Microsoft.Extensions.Caching.Memory.IMemoryCache`, xUnit, Moq, FluentAssertions.

---

## File Structure

Files modified by this plan (no new files):

| Path | Change |
|------|--------|
| `backend/src/Anela.Heblo.Application/Features/FinancialOverview/Services/FinancialAnalysisService.cs` | Delete `_refreshLock` field (line 22) and the `lock (_refreshLock) { ... }` wrapper around the throttle check (lines 109-117). |
| `backend/test/Anela.Heblo.Tests/Application/FinancialOverview/FinancialAnalysisServiceTests.cs` | Add one regression test (`RefreshFinancialDataAsync_WhenLastRefreshWithinThrottleWindow_DoesNotInvokeDownstreamServices`) that seeds `financial_last_refresh` inside the 10-minute window and asserts no downstream calls. |

Out of scope and **must not be touched**:
- `backend/src/Anela.Heblo.Application/Features/Catalog/CostProviders/ManufactureBasedMaterialCostProvider.cs` — also defines a `_refreshLock`, but it is a different (static `SemaphoreSlim`) lock in an unrelated module.
- `backend/src/Anela.Heblo.Application/Features/FinancialOverview/FinancialOverviewModule.cs` — DI registration stays Scoped.
- `docs/plans/2025-12-21-margin-cache-architecture.md` — historical planning doc that references `_refreshLock`; leave as-is.
- Every other method in `FinancialAnalysisService.cs` (`GetFinancialOverviewAsync`, `RefreshMonthlyDataAsync`, `GetCacheStatus`, `GetCachedFinancialOverview`, `GetHybridWithCurrentMonthAsync`, `GetFinancialOverviewRealTimeAsync`, `CreateStockSummary`) is untouched.

---

## Background context the implementer needs

### Why this code exists in the first place
`FinancialAnalysisService.RefreshFinancialDataAsync` is invoked from two places:
1. **`BackgroundRefreshSchedulerService.RunTaskLoop`** — periodically calls registered refresh tasks. Per `BackgroundRefreshSchedulerService.cs:86`, every tick creates a fresh `IServiceProvider.CreateScope()` and resolves a brand-new `IFinancialAnalysisService` from it.
2. **User-triggered cache-miss paths** — e.g. an HTTP request whose handler calls `RefreshFinancialDataAsync` to populate the cache. Each HTTP request also has its own DI scope.

Because each invocation gets its own service instance, the `_refreshLock` field declared on the instance is created fresh per call and never contended. Removing it does not change observable behavior in any code path.

### Why the throttle still works without the lock
The throttle check on lines 111-116 reads `LAST_REFRESH_CACHE_KEY` from `IMemoryCache` and exits early if a refresh happened within the last 10 minutes. `IMemoryCache.Get<T>` and `IMemoryCache.Set` are thread-safe on `MemoryCache`, so the read and the write at line 143 do not need an extra `lock`. There is still a race window between read (line 111) and write (line 143) during which two callers could both pass the gate — but the lock never closed that window either (each caller holds its own lock), so removing it does not regress anything. The refresh is idempotent (writes the same keys with the same TTLs), so concurrent overlapping refreshes converge on the same cache state. See `spec.r1.md` NFR-2 for the documented honest semantics.

### Why we are not promoting the service to Singleton
`IStockValueService` is registered Scoped (`FinancialOverviewModule.cs:19`). Injecting a Scoped service into a Singleton creates a captive-dependency bug. Refactoring scope handling inside `RefreshFinancialDataAsync` to resolve `IStockValueService` per-call is a larger change explicitly out of scope per `spec.r1.md`.

### Validation gate (from `CLAUDE.md`)
Before declaring the task done:
- `dotnet build` clean
- `dotnet format` clean
- `dotnet test` green for `backend/test/Anela.Heblo.Tests`

No E2E, no migrations, no FE changes, no config changes.

---

### Task 1: Add the regression test that locks in the throttle behavior

This test is added **before** the lock removal. It must pass against the current (lock-present) code so we can prove the throttle short-circuits downstream services without depending on the lock. After the lock removal in Task 2, it must continue to pass — that is the regression guarantee.

**Files:**
- Modify: `backend/test/Anela.Heblo.Tests/Application/FinancialOverview/FinancialAnalysisServiceTests.cs` (add a new `[Fact]` at the end of the class, after the existing `GetFinancialOverviewAsync_WhenDepartmentsExcludedAndCurrentMonthRequested_UsesFullRealTime` test and before the private `SeedCacheForMonth` helper)

#### Step 1.1: Add the regression test

- [ ] **Add this `[Fact]` to `FinancialAnalysisServiceTests.cs`, immediately after the test method that ends at line 172 (the `GetFinancialOverviewAsync_WhenDepartmentsExcludedAndCurrentMonthRequested_UsesFullRealTime` test) and before the private `SeedCacheForMonth` helper declared at line 174:**

```csharp
[Fact]
public async Task RefreshFinancialDataAsync_WhenLastRefreshWithinThrottleWindow_DoesNotInvokeDownstreamServices()
{
    // Arrange: seed the last-refresh timestamp inside the 10-minute throttle window.
    // The throttle check uses the cache key "financial_last_refresh" and skips the refresh
    // when the timestamp is younger than 10 minutes.
    _memoryCache.Set("financial_last_refresh", DateTime.UtcNow.AddMinutes(-1), TimeSpan.FromHours(24));

    // Act
    await _service.RefreshFinancialDataAsync(startDate: null, endDate: null);

    // Assert: the throttle short-circuits before any downstream call.
    _ledgerServiceMock.Verify(
        x => x.GetLedgerItems(
            It.IsAny<DateTime>(),
            It.IsAny<DateTime>(),
            It.IsAny<IEnumerable<string>?>(),
            It.IsAny<IEnumerable<string>?>(),
            It.IsAny<string?>(),
            It.IsAny<CancellationToken>()),
        Times.Never,
        "throttle should prevent any ledger query when last refresh was less than 10 minutes ago");

    _stockValueServiceMock.Verify(
        x => x.GetStockValueChangesAsync(
            It.IsAny<DateTime>(),
            It.IsAny<DateTime>(),
            It.IsAny<CancellationToken>()),
        Times.Never,
        "throttle should prevent any stock-value query when last refresh was less than 10 minutes ago");
}
```

Notes for the implementer:
- The `_ledgerServiceMock`, `_stockValueServiceMock`, `_memoryCache`, and `_service` fields are already set up in the constructor at lines 20-53.
- The 6-argument shape of `GetLedgerItems` (start, end, debit prefixes, credit prefixes, department, ct) matches the existing setup at lines 36-44, so do not modify or duplicate the constructor setup.
- The cache key literal `"financial_last_refresh"` matches the `LAST_REFRESH_CACHE_KEY` constant defined in `FinancialAnalysisService.cs:20`. Keep the literal — do not try to import the private constant.
- The 24-hour TTL on the `_memoryCache.Set` call mirrors how the production code writes this key at `FinancialAnalysisService.cs:143`.

#### Step 1.2: Run the new test against current (unmodified) code

- [ ] **Run the test and confirm it passes against the current code.**

Run from the repo root:

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~RefreshFinancialDataAsync_WhenLastRefreshWithinThrottleWindow_DoesNotInvokeDownstreamServices"
```

Expected output: 1 passed, 0 failed. If it fails, **stop** — that means either (a) the test was not added correctly, or (b) the throttle is not behaving as `spec.r1.md` claims, both of which need investigation before continuing.

#### Step 1.3: Run the whole touched test class to confirm no regression

- [ ] **Run all tests in the file and confirm they all pass.**

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~FinancialAnalysisServiceTests"
```

Expected: all green (4 existing tests + 1 new = 5 passed).

#### Step 1.4: Commit the new test

- [ ] **Commit the new test on its own.** Two commits keeps the lock-removal diff clean.

```bash
git add backend/test/Anela.Heblo.Tests/Application/FinancialOverview/FinancialAnalysisServiceTests.cs
git commit -m "test: pin FinancialAnalysisService refresh throttle behavior"
```

---

### Task 2: Remove the misleading `_refreshLock`

Spec requirement: FR-1, FR-2, FR-3.

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/FinancialOverview/Services/FinancialAnalysisService.cs`
  - Delete line 22 (the `_refreshLock` field).
  - Replace lines 109-117 (the `lock (_refreshLock) { ... }` block) with the bare throttle check.

#### Step 2.1: Delete the `_refreshLock` field

- [ ] **Remove line 22 from `FinancialAnalysisService.cs`.**

Find and delete this exact line (preserve the blank line before line 24 so the spacing between the cache-key constants block and the constructor remains a single blank line):

```csharp
    private readonly object _refreshLock = new();
```

The block from lines 18-24 must look like this **after** the edit (no `_refreshLock` line):

```csharp
    private const string MONTHLY_DATA_CACHE_KEY_PREFIX = "financial_monthly_data_";
    private const string STOCK_DATA_CACHE_KEY_PREFIX = "financial_stock_data_";
    private const string LAST_REFRESH_CACHE_KEY = "financial_last_refresh";

    public FinancialAnalysisService(
```

#### Step 2.2: Remove the lock wrapper around the throttle check

- [ ] **Replace the entire block at lines 109-117 (the `lock (_refreshLock) { ... }` block) with the unwrapped throttle check.**

Replace this:

```csharp
        lock (_refreshLock)
        {
            var lastRefresh = _memoryCache.Get<DateTime?>(LAST_REFRESH_CACHE_KEY) ?? DateTime.MinValue;
            if (DateTime.UtcNow - lastRefresh < TimeSpan.FromMinutes(10)) // Prevent too frequent refreshes
            {
                _logger.LogDebug("Skipping refresh, last refresh was too recent");
                return;
            }
        }
```

with this:

```csharp
        var lastRefresh = _memoryCache.Get<DateTime?>(LAST_REFRESH_CACHE_KEY) ?? DateTime.MinValue;
        if (DateTime.UtcNow - lastRefresh < TimeSpan.FromMinutes(10)) // Prevent too frequent refreshes
        {
            _logger.LogDebug("Skipping refresh, last refresh was too recent");
            return;
        }
```

Notes for the implementer:
- Indentation: the replacement body is one level shallower than the original. The opening of `RefreshFinancialDataAsync` is `public async Task RefreshFinancialDataAsync(...)`, so the body is indented with 8 spaces (two levels: class + method). Do **not** leave the body indented at 12 spaces (the depth it had inside `lock { ... }`).
- Preserve the `// Prevent too frequent refreshes` inline comment exactly. It is an existing comment on the original line — the "surgical changes" rule in CLAUDE.md says to preserve unrelated content. (Per spec FR-2 the throttle behavior must be byte-identical apart from the lock removal.)
- Preserve the `_logger.LogDebug("Skipping refresh, last refresh was too recent")` call verbatim — same message, same log level.
- Do not touch anything inside the `try` block that follows. Lines from `try` (was line 119) onward stay byte-identical.

#### Step 2.3: Verify no `_refreshLock` references remain in this file or the test file

- [ ] **Grep the modified files to confirm `_refreshLock` is gone from both.**

```bash
grep -n "_refreshLock" \
  backend/src/Anela.Heblo.Application/Features/FinancialOverview/Services/FinancialAnalysisService.cs \
  backend/test/Anela.Heblo.Tests/Application/FinancialOverview/FinancialAnalysisServiceTests.cs
```

Expected output: nothing (no lines printed, exit code 1 from `grep`).

If you see any match, the edit was incomplete — re-run the edit step.

#### Step 2.4: Verify no `lock` keyword remains in `FinancialAnalysisService.cs`

- [ ] **Per spec FR-2, no `lock` keyword should remain in the file.**

```bash
grep -nw "lock" \
  backend/src/Anela.Heblo.Application/Features/FinancialOverview/Services/FinancialAnalysisService.cs
```

Expected output: nothing.

#### Step 2.5: Build the backend solution

- [ ] **Run `dotnet build` and confirm zero new warnings.**

```bash
dotnet build backend/Anela.Heblo.sln
```

Expected: `Build succeeded.` with zero warnings introduced by this change. If the build introduces a new warning (e.g. an "unused field" hint that did not exist before), investigate before continuing — the spec FR-1 requires zero new warnings.

#### Step 2.6: Apply formatter

- [ ] **Run `dotnet format` so whitespace matches project conventions.**

```bash
dotnet format backend/Anela.Heblo.sln
```

Expected: exit code 0. If formatting produces changes inside the modified file, that is expected — accept them. If it produces changes in unrelated files, **stop and review**: the project's "surgical changes" rule says we should not be reformatting files we did not touch. In that case revert the unrelated formatter changes with `git checkout -- <path>` and proceed.

#### Step 2.7: Run the touched test project

- [ ] **Run all tests in `Anela.Heblo.Tests` and confirm they all pass.**

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj
```

Expected: all green. The new regression test from Task 1 must still pass — that is the whole point of adding it before the lock removal.

If any other test fails, it most likely depends indirectly on the lock object's existence or timing; investigate before continuing. (Per arch-review, none of the existing tests reference `_refreshLock`, so a failure here is unexpected.)

#### Step 2.8: Confirm DI registration is unchanged

- [ ] **Spec FR-3 requires `FinancialOverviewModule.cs` to be untouched. Confirm with `git diff`.**

```bash
git diff backend/src/Anela.Heblo.Application/Features/FinancialOverview/FinancialOverviewModule.cs
```

Expected output: nothing (empty diff).

#### Step 2.9: Commit the lock removal

- [ ] **Commit the lock-removal diff.**

```bash
git add backend/src/Anela.Heblo.Application/Features/FinancialOverview/Services/FinancialAnalysisService.cs
git commit -m "refactor: remove misleading instance-level lock in FinancialAnalysisService

The _refreshLock field on FinancialAnalysisService was decorative.
The service is registered Scoped, so each HTTP request and each
IServiceProvider.CreateScope() (used by BackgroundRefreshSchedulerService)
gets its own instance with its own _refreshLock — concurrent callers
never contended on the same monitor.

The real cross-request gate is the IMemoryCache entry at the
'financial_last_refresh' key, which IMemoryCache makes thread-safe
on its own. Removing the lock leaves observable behavior unchanged
and removes the false implication of cross-instance coordination."
```

---

### Task 3: Final verification

#### Step 3.1: Whole-solution build

- [ ] **Run a clean build of the full solution to confirm no regressions outside the modified module.**

```bash
dotnet build backend/Anela.Heblo.sln
```

Expected: `Build succeeded.` with zero new warnings.

#### Step 3.2: Run the full backend test suite

- [ ] **Run all backend tests.**

```bash
dotnet test backend/Anela.Heblo.sln
```

Expected: all green. If a test outside `FinancialAnalysisServiceTests` fails, it almost certainly is unrelated (the diff only touches one private field and one synchronization wrapper) — investigate and report rather than papering over.

#### Step 3.3: Confirm the final diff is exactly the two intended files

- [ ] **Print the changed-file list since main and confirm it is exactly the two files in scope.**

```bash
git diff --name-only main...HEAD
```

Expected output (exactly these two paths, in some order):

```
backend/src/Anela.Heblo.Application/Features/FinancialOverview/Services/FinancialAnalysisService.cs
backend/test/Anela.Heblo.Tests/Application/FinancialOverview/FinancialAnalysisServiceTests.cs
```

If any other file appears (especially `ManufactureBasedMaterialCostProvider.cs`, `FinancialOverviewModule.cs`, or anything in `docs/plans/`), **stop** — that file should not have been touched. Inspect with `git diff` and `git checkout -- <path>` to revert anything unintended.

#### Step 3.4: Confirm the FinancialAnalysisService diff is exactly the lock removal

- [ ] **Inspect the `FinancialAnalysisService.cs` diff and confirm it touches only the two regions specified by FR-1 and FR-2.**

```bash
git diff main...HEAD -- backend/src/Anela.Heblo.Application/Features/FinancialOverview/Services/FinancialAnalysisService.cs
```

Expected shape:
- One removed line: `    private readonly object _refreshLock = new();` (plus possibly a blank line)
- The lock wrapper (`lock (_refreshLock)`, the opening `{`, and the closing `}`) removed; the throttle body re-indented by 4 spaces (one level shallower).
- No other changes — no touched whitespace anywhere else, no touched method body anywhere else.

If the diff is larger than that, undo the extra changes before proceeding to PR.

---

## Spec coverage map

| Spec requirement | Implemented by |
|------------------|----------------|
| FR-1: remove `_refreshLock` field | Step 2.1, verified by Step 2.3 |
| FR-2: remove the `lock` block but keep the throttle check (same log message, same early return, same successful-refresh path) | Step 2.2, verified by Step 2.4 |
| FR-3: no other code path changes; `FinancialOverviewModule.cs` unchanged; `IFinancialAnalysisService` surface unchanged | Step 2.8 confirms DI module untouched; Steps 3.3 + 3.4 confirm diff is limited to the two intended files and regions |
| FR-4: regression test for second call within 10-minute window being a no-op (no new ledger / stock-value invocations) | Task 1 (added before lock removal); verified again in Step 2.7 and Step 3.2 |
| NFR-1: no measurable performance change | Inherent to the change; no benchmark required by spec |
| NFR-2: honest concurrency semantics | Documented in this plan's "Background context"; spec NFR-2 explicitly accepts the residual best-effort throttle race |
| NFR-3: no security impact | No auth / input / secrets touched; verified by Step 3.3's file-list assertion |
| NFR-4: backward compatibility | No API / contract / config / schema change; verified by Step 2.8 |
| Out of Scope: do not promote to Singleton, do not add new at-most-one-in-flight guarantee, do not change throttle duration, do not refactor surrounding method | The plan does none of these; Step 3.4's diff-shape check is the guardrail |

## Pipeline note

This plan was authored as part of an automated pipeline. Per the pipeline instructions, the execution-handoff prompt is intentionally omitted — the file content above is the deliverable artifact.
