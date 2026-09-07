# Implementation: rewire-adapter-to-repository

## What was implemented
`InvoiceImportStatisticsSourceAdapter` no longer injects `ApplicationDbContext` or
runs an EF Core query directly. It is now a thin pass-through that injects
`IIssuedInvoiceRepository` and forwards `GetDailyCountsAsync(startDate, endDate,
dateType, cancellationToken)` to the repository, matching the interface contract
of `IInvoiceImportStatisticsSource` exactly (verified against the real interface,
not assumed) and the existing `InvoiceConsumptionSourceAdapter` pattern.

`IIssuedInvoiceRepository.GetDailyCountsAsync` (added by the prior task) has the
identical signature `Task<IReadOnlyList<DailyInvoiceCount>> GetDailyCountsAsync(DateTime, DateTime, ImportDateType, CancellationToken = default)`,
so the adapter body is a single delegating call.

The unit tests were rewritten to mock `IIssuedInvoiceRepository` (Moq) instead of
standing up an in-memory `ApplicationDbContext`, following the exact pattern used
by `InvoiceConsumptionSourceAdapterTests`.

## Files created/modified
- `backend/src/Anela.Heblo.Application/Features/Invoices/Infrastructure/InvoiceImportStatisticsSourceAdapter.cs` — replaced the EF Core query implementation (102 lines) with a thin delegator to `IIssuedInvoiceRepository` (21 lines). No more references to `ApplicationDbContext`, `Anela.Heblo.Persistence`, or `Microsoft.EntityFrameworkCore`.
- `backend/test/Anela.Heblo.Tests/Features/Invoices/Infrastructure/InvoiceImportStatisticsSourceAdapterTests.cs` — replaced the `IDisposable` + in-memory-DbContext test fixture (5 tests exercising grouping/gap-fill logic that now lives in the repository) with 3 tests that mock `IIssuedInvoiceRepository` and verify argument forwarding, unchanged pass-through of results, and correct `ImportDateType` forwarding.

## Tests
- `InvoiceImportStatisticsSourceAdapterTests.cs` (3 tests, all passing):
  - `GetDailyCountsAsync_ForwardsArgumentsToRepository`
  - `GetDailyCountsAsync_ReturnsRepositoryResultUnchanged`
  - `GetDailyCountsAsync_PassesLastSyncTimeDateType_WhenRequested`

## How to verify
```bash
cd backend
dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~InvoiceImportStatisticsSourceAdapterTests" -c Release
# => Passed! - Failed: 0, Passed: 3, Skipped: 0, Total: 3

grep -n "ApplicationDbContext\|EntityFrameworkCore\|Anela.Heblo.Persistence" \
  src/Anela.Heblo.Application/Features/Invoices/Infrastructure/InvoiceImportStatisticsSourceAdapter.cs
# => no output

cd .. && dotnet build Anela.Heblo.sln -c Release
# => Build succeeded, 0 Errors
```

## Notes
- **Environment quirk (not a code issue):** in this sandbox, `dotnet test`/`dotnet build` in the default **Debug** configuration reliably hangs inside `Anela.Heblo.API.csproj`'s `GenerateAccessMatrix` pre-build target (a `<Exec>` running `dotnet run --project tools/Anela.Heblo.AccessMatrixGen`). The nested `dotnet run` completes and writes its output, but the outer MSBuild `<Exec>` task never observes EOF on the captured stdout pipe and blocks forever (confirmed via `/proc/<pid>/stat` — zero CPU progress across minutes, not merely slow). This reproduced identically after clearing NuGet/build-server state, with node reuse off, with shared compilation off, and with the generator tool pre-warmed — so it isn't restore-lock contention, just a pipe/fd-inheritance hazard specific to this sandboxed process environment when nesting `dotnet run` inside an MSBuild `Exec` task. Building/testing with **`-c Release`** (which the `GenerateAccessMatrix` target's `Condition="'$(Configuration)' == 'Debug'"` skips entirely) avoids the hang completely and was used for all verification here. No source or project files were changed to work around this — it's purely an invocation flag for this session. Worth flagging to the user/pipeline maintainer since it will affect any other agent runs in this same sandboxed environment; CI/dev-machine builds (which normally run Debug) are presumably unaffected since the codebase's own leftover `test_full_results.txt`/`test_result.txt` show this same target completing normally on a real developer machine.
- `InvoicesModule.cs` DI registration required no change: `IIssuedInvoiceRepository` is already registered `Scoped` (used by `InvoiceConsumptionSourceAdapter` already), and `IInvoiceImportStatisticsSource` is registered via `services.AddScoped<IInvoiceImportStatisticsSource, InvoiceImportStatisticsSourceAdapter>()` with no explicit factory — the container resolves the new constructor automatically. One stale comment remains on that registration line ("Scoped because the adapter wraps ApplicationDbContext (also Scoped)") that's no longer accurate now that it wraps the repository instead — left untouched per the "surgical changes" rule since `InvoicesModule.cs` isn't in this task's file list; flagging it here rather than editing it.
- `AnalyticsRepository.cs` (the sole consumer of `IInvoiceImportStatisticsSource`) required no change — it only calls the interface method, which is unchanged.
- Full solution build (`dotnet build Anela.Heblo.sln -c Release`) succeeded with 0 errors, confirming no other caller depends on the old `ApplicationDbContext`-based constructor.

## PR Summary
Removes the Application layer's direct `ApplicationDbContext`/EF Core dependency from `InvoiceImportStatisticsSourceAdapter`, turning it into a thin adapter over the new `IIssuedInvoiceRepository.GetDailyCountsAsync` method (added by the preceding task), and rewrites its unit tests to mock the repository rather than standing up an in-memory database — bringing it in line with the existing `InvoiceConsumptionSourceAdapter` pattern and keeping persistence concerns out of the Application layer.

### Changes
- `backend/src/Anela.Heblo.Application/Features/Invoices/Infrastructure/InvoiceImportStatisticsSourceAdapter.cs` — now injects `IIssuedInvoiceRepository` and delegates `GetDailyCountsAsync` to it instead of querying `ApplicationDbContext` directly.
- `backend/test/Anela.Heblo.Tests/Features/Invoices/Infrastructure/InvoiceImportStatisticsSourceAdapterTests.cs` — rewritten to mock `IIssuedInvoiceRepository` with Moq instead of an in-memory `ApplicationDbContext`.

## Status
DONE
