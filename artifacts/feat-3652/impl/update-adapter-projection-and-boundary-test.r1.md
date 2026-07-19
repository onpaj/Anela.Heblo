# Implementation: update-adapter-projection-and-boundary-test

## What was implemented

Two changes, per the task context:

1. `BankStatementStatisticsSourceAdapter.GetDailyStatisticsAsync` — the public method signature (implementing the Analytics-owned `IBankStatementStatisticsSource` contract) is unchanged. Internally, it now calls the Bank repository's new `GetDailyCountsAsync(startDate, endDate, dateType == BankStatementDateType.StatementDate, cancellationToken)` instead of the old `GetDailyStatisticsAsync`, then maps each returned `BankDailyCount` to a `DailyBankStatementStatistics { Date, ImportCount, TotalItemCount }`. The existing gap-fill loop (filling every date in `[startDate, endDate]` with a zero-count row when absent) is unchanged.
2. Added a new `"Bank (Domain) -> Analytics"` `ModuleBoundaryRule` to `ModuleBoundariesTests.cs`, inserted immediately after the existing `"Analytics (Domain) -> Bank"` entry and before `"Catalog -> Logistics"`. It inspects `Anela.Heblo.Domain.Features.Bank` and forbids references to `Anela.Heblo.Domain.Features.Analytics` / `Anela.Heblo.Application.Features.Analytics` / `Anela.Heblo.Persistence.Analytics`, with an empty allowlist, mirroring the existing reverse-direction rule exactly.

## Files created/modified
- `backend/src/Anela.Heblo.Application/Features/Bank/Infrastructure/BankStatementStatisticsSourceAdapter.cs` — internal call site + projection updated; public contract and `using` statements unchanged (this file legitimately needs both Bank and Analytics namespaces as the designed cross-module seam).
- `backend/test/Anela.Heblo.Tests/Architecture/ModuleBoundariesTests.cs` — added the `"Bank (Domain) -> Analytics"` rule.

## Tests

- `BankStatementStatisticsSourceAdapterTests` (5 tests, unmodified) — all pass, confirming the adapter's mapping/gap-fill behavior is unchanged.
- `ModuleBoundariesTests` (29 tests total, including the new rule) — all pass.
- Full `Features.Bank` test suite — 121 passed, 8 failed. The 8 failures are all `BankStatementImportRepositoryIntegrationTests` cases requiring Docker (Testcontainers PostgreSQL fixture); Docker is unavailable in this sandbox environment. Pre-existing limitation, unrelated to this change (these tests don't exercise `GetDailyCountsAsync`/`GetDailyStatisticsAsync` at all).
- Full backend suite (`dotnet test`, no filter) — 5771 passed, 76 failed, 4 skipped. All 76 failures follow the identical Docker/Testcontainers-unavailable pattern, spread across many unrelated modules (Article, Catalog, Invoices, Leaflet, KnowledgeBase, GridLayouts, Smartsupp, Bank) — confirmed by inspecting the full failure list and error messages (all raise `System.ArgumentException: Docker is either not running or misconfigured`). None are regressions introduced by this change.

## How to verify

```bash
cd backend
dotnet build                                    # 0 errors
dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --no-build --filter "FullyQualifiedName~BankStatementStatisticsSourceAdapterTests"   # 5/5 pass
dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --no-build --filter "FullyQualifiedName~ModuleBoundariesTests"                        # 29/29 pass
dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --no-build --filter "FullyQualifiedName~Features.Bank"                                # 121 pass, 8 fail (Docker-only)
cd ..
dotnet format Anela.Heblo.sln --verify-no-changes   # run from repo root (solution lives there, not in backend/); exit 0
```

Note: in this sandbox, running `dotnet test` **without** `--no-build` intermittently hung indefinitely in the implicit restore/build step (observed twice, ~6+ minutes with zero CPU progress, required killing the process and any stale MSBuild node-reuse servers via `pkill -9 -f dotnet`). Building explicitly first (`dotnet build`) and then running each test filter with `--no-build` avoided the hang and completed in seconds each time. This is an environment quirk, not a code issue — future validation in this repo/environment should prefer the build-then-`--no-build` pattern for `dotnet test`.

## Notes

- `IBankStatementStatisticsSource.cs` (Analytics-owned contract) was not touched, per scope.
- `AnalyticsRepository.cs` was not touched, per scope.
- `BankStatementStatisticsSourceAdapterTests.cs` was not edited — it passed unmodified as required.
- No migration, no DI registration change, no frontend/API changes were needed (confirmed).
- The only pre-existing, unrelated quirk observed: `dotnet build` prints a warning (exit code 134) from a build-time code-gen tool (`Anela.Heblo.AccessMatrixGen`) hitting a `System.Text.Json.JsonException` — this is a pre-existing warning-only issue in an unrelated tool, does not fail the build, and is untouched by this change.

## PR Summary

Removed the Analytics-module dependency leak from Bank's `IBankStatementImportRepository` domain contract: the repository now returns a Bank-owned `BankDailyCount` record via `GetDailyCountsAsync(bool byStatementDate)`, and `BankStatementStatisticsSourceAdapter` (the designed cross-module seam) does the projection to the Analytics-owned `DailyBankStatementStatistics`, preserving all existing behavior including gap-fill. A new architecture-boundary test rule (`"Bank (Domain) -> Analytics"`) now enforces this direction going forward, closing the gap that let the original violation land undetected.

### Changes
- `backend/src/Anela.Heblo.Domain/Features/Bank/BankDailyCount.cs` — new record (added in an earlier task of this plan)
- `backend/src/Anela.Heblo.Domain/Features/Bank/IBankStatementImportRepository.cs` — `GetDailyStatisticsAsync` replaced with `GetDailyCountsAsync` (earlier task)
- `backend/src/Anela.Heblo.Persistence/Features/Bank/BankStatementImportRepository.cs` — EF Core implementation retyped (earlier task)
- `backend/src/Anela.Heblo.Application/Features/Bank/Infrastructure/BankStatementStatisticsSourceAdapter.cs` — call site + projection updated (this task)
- `backend/test/Anela.Heblo.Tests/Architecture/ModuleBoundariesTests.cs` — new "Bank (Domain) -> Analytics" boundary rule (this task)

## Status
DONE
