# Code Review: add-bankdailycount-and-update-domain-contract

## Summary
The implementation exactly matches the task spec: a new Bank-owned `BankDailyCount` record was created and `IBankStatementImportRepository` was updated to drop the Analytics dependency, replacing `GetDailyStatisticsAsync` with the Analytics-agnostic `GetDailyCountsAsync`. File contents, the commit, and the isolated Domain project build were all independently verified.

## Review Result: PASS

### task: add-bankdailycount-and-update-domain-contract
**Status:** PASS

## Verification performed
- `backend/src/Anela.Heblo.Domain/Features/Bank/BankDailyCount.cs` — matches spec exactly: `public record BankDailyCount(DateTime Date, int ImportCount, int TotalItemCount);`
- `backend/src/Anela.Heblo.Domain/Features/Bank/IBankStatementImportRepository.cs` — matches the spec's replacement content exactly: `using Anela.Heblo.Domain.Features.Analytics;` removed, `GetDailyStatisticsAsync(..., BankStatementDateType, ...)` replaced by `GetDailyCountsAsync(DateTime startDate, DateTime endDate, bool byStatementDate, CancellationToken cancellationToken = default)` returning `IReadOnlyList<BankDailyCount>`. All other interface members unchanged.
- `git log --oneline -3` confirms commit `4d5ded0 Bank: replace GetDailyStatisticsAsync with Analytics-agnostic GetDailyCountsAsync on IBankStatementImportRepository` exists.
- `cd backend && dotnet build src/Anela.Heblo.Domain/Anela.Heblo.Domain.csproj` succeeded: `0 Error(s)`, 87 warnings (all pre-existing CS8618/CS8602 nullability warnings in unrelated files, not introduced by this change).
- No remaining references to `Anela.Heblo.Domain.Features.Analytics`, `DailyBankStatementStatistics`, or `BankStatementDateType` in the modified interface file.

## Docs to Update
None — this is an internal Domain-layer contract change with no public API, CLI, or operational impact; per the task context, downstream consumers (EF Core implementation, Analytics adapter) are intentionally left broken and are addressed by subsequent tasks in this plan.

## Overall Notes
Task correctly scoped itself to the Domain-layer contract only, as instructed, and did not attempt to fix the now-broken Persistence/Application-layer callers — that is explicitly deferred to later tasks per the task context. No issues found.
