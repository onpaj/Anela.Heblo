# Implementation: remove-redundant-whenall

## What was implemented

Removed the redundant, discarded-result `await Task.WhenAll(startStockTasks.Concat(endStockTasks));` line from `CalculateMonthlyStockChangeAsync` in `FinancialOverviewStockValueAdapter.cs`. This line awaited the combined set of start/end stock tasks but never used the result — the two immediately following lines (`await Task.WhenAll(startStockTasks)` and `await Task.WhenAll(endStockTasks)`) already await the same underlying tasks and are the only ones whose results are used. Removing it is a pure no-op for behavior, correctness, and performance (all six tasks are already started at creation time, before any await), and brings the method's structure in line with the sibling method `GetStockValueChangeForPeriodAsync` in the same file, which already implements the identical pattern without a redundant combined await.

## Files created/modified

- `backend/src/Anela.Heblo.Application/Features/Catalog/Infrastructure/FinancialOverviewStockValueAdapter.cs` — deleted the single redundant `await Task.WhenAll(startStockTasks.Concat(endStockTasks));` line (plus the blank line that sat below it, so exactly one blank line remains, matching surrounding style). No other line in the file was touched. `git diff` confirms exactly a two-line removal (the statement + one blank line).

## Tests

No new or modified tests — the deleted line was unreachable dead code with no observable behavior, so none were required. Ran the existing suite that covers this code path:

- `backend/test/Anela.Heblo.Tests/Application/FinancialOverview/StockValueServiceTests.cs` — exercises `GetStockValueChangesAsync`, which calls `CalculateMonthlyStockChangeAsync` once per month with mocked `IErpStockClient`/`IProductPriceErpClient`.

## How to verify

1. `dotnet build Anela.Heblo.sln` from the repo root — succeeded, 0 errors (only pre-existing warnings unrelated to this file/change).
2. `dotnet test backend/test/Anela.Heblo.Tests/bin/Debug/net8.0/Anela.Heblo.Tests.dll --filter FullyQualifiedName~StockValueServiceTests --nologo` (run directly against the already-built test DLL to avoid the sandbox's very slow per-invocation MSBuild up-to-date check on `dotnet test`/`dotnet build` against the .csproj — the DLL was rebuilt after the source edit, confirmed via file mtimes) — `Passed! - Failed: 0, Passed: 4, Skipped: 0, Total: 4`.
3. `dotnet format Anela.Heblo.sln --verify-no-changes --include backend/src/Anela.Heblo.Application/Features/Catalog/Infrastructure/FinancialOverviewStockValueAdapter.cs` — exit code 0, no output (no formatting issues).
4. `git diff` for the changed file shows exactly the intended two-line removal (the statement plus one blank line), nothing else.

## Notes

The sandbox environment was under heavy CPU contention during this task (load average ~5.8 on 4 cores); `dotnet test`/`dotnet format` invoked against the `.csproj`/`.sln` repeatedly stalled for 5-10+ minutes each on MSBuild's up-to-date evaluation across the whole solution graph, even with warm build caches, and two attempts had to be killed. Working around this by running `dotnet test` directly against the already-built test DLL (skipping the MSBuild evaluation step) and scoping `dotnet format --verify-no-changes` to just the changed file via `--include` produced fast, clean, conclusive results. This is an environment/tooling-speed observation only — it did not affect the correctness of the change or the validity of the verification, since the DLL used was confirmed built after the source edit and the same solution-wide `dotnet format` target was used, just scoped.

No deviations from the task spec. No documentation needs updating — this is an internal, behavior-preserving cleanup of dead code with no public API, config, or operational surface change.

## PR Summary

Removed a redundant, discarded-result `await Task.WhenAll(startStockTasks.Concat(endStockTasks));` line from `CalculateMonthlyStockChangeAsync` in `FinancialOverviewStockValueAdapter.cs`. The two awaits immediately following it already await the same tasks and are the only ones whose results are used to compute the monthly stock change; the removed line awaited nothing new and used no result, so this is a pure no-op for behavior and performance. Brings this method in line with the sibling method `GetStockValueChangeForPeriodAsync` in the same file, which already implements the identical start/end concurrent-fetch pattern without a redundant combined await.

### Changes
- `backend/src/Anela.Heblo.Application/Features/Catalog/Infrastructure/FinancialOverviewStockValueAdapter.cs` — removed the redundant combined `Task.WhenAll` await line (and its trailing blank line) from `CalculateMonthlyStockChangeAsync`

## Status
DONE
