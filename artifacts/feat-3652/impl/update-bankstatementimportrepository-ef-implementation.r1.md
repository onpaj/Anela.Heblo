# Implementation: update-bankstatementimportrepository-ef-implementation

## What was implemented
Updated the EF Core implementation `BankStatementImportRepository` to match the already-updated
`IBankStatementImportRepository` domain interface: removed the `using Anela.Heblo.Domain.Features.Analytics;`
import, and replaced `GetDailyStatisticsAsync(DateTime, DateTime, BankStatementDateType, CancellationToken)`
with `GetDailyCountsAsync(DateTime, DateTime, bool byStatementDate, CancellationToken)`. The method now
branches on the `byStatementDate` boolean (ternary) instead of switching on the `BankStatementDateType`
enum, and returns `BankDailyCount` record instances instead of `DailyBankStatementStatistics`. Grouping,
filtering, and aggregation logic (group by Year/Month/Day of the selected date column, `ImportCount` = row
count, `TotalItemCount` = sum of `ItemCount`) is unchanged — only the branch condition and return type
changed.

## Files created/modified
- `backend/src/Anela.Heblo.Persistence/Features/Bank/BankStatementImportRepository.cs` — dropped the
  Analytics `using`; retyped `GetDailyStatisticsAsync` to `GetDailyCountsAsync`, returning
  `IReadOnlyList<BankDailyCount>` and branching on `bool byStatementDate` instead of the
  `BankStatementDateType` enum.

## Tests
No new/changed tests in this task. `backend/test/Anela.Heblo.Tests/Features/Bank/BankStatementStatisticsSourceAdapterTests.cs`
was left untouched and was NOT run — the `Application` layer's `BankStatementStatisticsSourceAdapter` still
calls the old `GetDailyStatisticsAsync` method name and will not compile until the sibling adapter task is
applied. Test execution is deferred to that task.

## How to verify
1. `grep -n "Anela.Heblo.Domain.Features.Analytics\|DailyBankStatementStatistics\|BankStatementDateType" backend/src/Anela.Heblo.Persistence/Features/Bank/BankStatementImportRepository.cs`
   — expect no output (exit code 1). Confirmed.
2. `cd backend && dotnet build src/Anela.Heblo.Persistence/Anela.Heblo.Persistence.csproj`
   — expect `Build succeeded.` with 0 errors. Confirmed: 0 errors, 88 pre-existing warnings unrelated to
   this change.

## Notes
No deviations from the task context's specified before/after code. The file matched the documented content
exactly prior to editing (199 lines, using block at 1-3, method at 143-195), so both edits applied cleanly.
Full-solution build was intentionally not attempted, per task scope — the `Application` project (adapter)
still references the old method name and will fail to compile until the next task lands.

## PR Summary
Retyped `BankStatementImportRepository.GetDailyStatisticsAsync` to `GetDailyCountsAsync`, switching from
the `Analytics.BankStatementDateType` enum parameter to a `bool byStatementDate` parameter and returning
`BankDailyCount` instead of `DailyBankStatementStatistics`, matching the already-updated domain interface.

### Changes
- `backend/src/Anela.Heblo.Persistence/Features/Bank/BankStatementImportRepository.cs` — removed the
  Analytics using directive; replaced `GetDailyStatisticsAsync` with `GetDailyCountsAsync`, preserving
  identical grouping/filtering/aggregation semantics.

## Status
DONE
