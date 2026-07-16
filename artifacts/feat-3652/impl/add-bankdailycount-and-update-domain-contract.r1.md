# Implementation: add-bankdailycount-and-update-domain-contract

## What was implemented
Introduced a new Bank-owned `BankDailyCount` record in the Domain layer and updated
`IBankStatementImportRepository` to replace the Analytics-coupled `GetDailyStatisticsAsync` method
(which used the Analytics enum `BankStatementDateType` and returned Analytics type
`DailyBankStatementStatistics`) with an Analytics-agnostic `GetDailyCountsAsync` method that takes a
plain `bool byStatementDate` and returns `IReadOnlyList<BankDailyCount>`. The
`using Anela.Heblo.Domain.Features.Analytics;` import was removed from the interface file, fixing the
module-boundary violation (Domain layer no longer depends on the Analytics module's types).

## Files created/modified
- `backend/src/Anela.Heblo.Domain/Features/Bank/BankDailyCount.cs` — new record: `public record BankDailyCount(DateTime Date, int ImportCount, int TotalItemCount);`
- `backend/src/Anela.Heblo.Domain/Features/Bank/IBankStatementImportRepository.cs` — removed `using Anela.Heblo.Domain.Features.Analytics;`; replaced `GetDailyStatisticsAsync(DateTime, DateTime, BankStatementDateType, CancellationToken)` returning `IReadOnlyList<DailyBankStatementStatistics>` with `GetDailyCountsAsync(DateTime, DateTime, bool byStatementDate, CancellationToken)` returning `IReadOnlyList<BankDailyCount>`. All other interface members unchanged.

## Tests
N/A — Domain project build check only, no new tests in this task.

## How to verify
```bash
grep -n "Anela.Heblo.Domain.Features.Analytics\|DailyBankStatementStatistics\|BankStatementDateType" backend/src/Anela.Heblo.Domain/Features/Bank/IBankStatementImportRepository.cs
# expect: no output, exit code 1

cd backend && dotnet build src/Anela.Heblo.Domain/Anela.Heblo.Domain.csproj
# expect: Build succeeded, 0 Errors
```
Both verification steps were run and passed: the grep found no matches, and the Domain project build
succeeded with 0 errors (87 pre-existing warnings, unrelated to this change).

## Notes
As documented in the task context, the full solution is expected to NOT build after this change — the
EF Core repository implementation and the Analytics adapter still reference the old
`GetDailyStatisticsAsync` method/types. That is out of scope for this task and will be addressed by
subsequent tasks in the plan. Only the Domain project build was verified here, per instructions.

## PR Summary
Fixes a module-boundary violation by introducing a Bank-owned `BankDailyCount` record and replacing
`IBankStatementImportRepository.GetDailyStatisticsAsync` (which depended on Analytics-owned types) with
an Analytics-agnostic `GetDailyCountsAsync` method.

### Changes
- `backend/src/Anela.Heblo.Domain/Features/Bank/BankDailyCount.cs` — new Bank-owned record with `Date`, `ImportCount`, `TotalItemCount`
- `backend/src/Anela.Heblo.Domain/Features/Bank/IBankStatementImportRepository.cs` — dropped Analytics `using`; replaced `GetDailyStatisticsAsync` with `GetDailyCountsAsync` using `bool byStatementDate` and returning `IReadOnlyList<BankDailyCount>`

## Status
DONE
