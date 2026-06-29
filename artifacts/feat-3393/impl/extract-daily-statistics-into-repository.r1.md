# Implementation: extract-daily-statistics-into-repository

## What was implemented

Moved the two EF Core LINQ query blocks that aggregate daily bank statement statistics out of `BankStatementStatisticsSourceAdapter` into a new `GetDailyStatisticsAsync` method on `IBankStatementImportRepository` and its implementation. The adapter now delegates to the repository and no longer holds a direct `ApplicationDbContext` dependency. Tests were updated to construct the adapter via `BankStatementImportRepository`.

## Files created/modified

- `backend/src/Anela.Heblo.Domain/Features/Bank/IBankStatementImportRepository.cs` — added `using Anela.Heblo.Domain.Features.Analytics;` and declared `GetDailyStatisticsAsync` on the interface
- `backend/src/Anela.Heblo.Persistence/Features/Bank/BankStatementImportRepository.cs` — added `using Anela.Heblo.Domain.Features.Analytics;` and implemented `GetDailyStatisticsAsync` with a switch expression over `BankStatementDateType`, projecting into `DailyBankStatementStatistics`
- `backend/src/Anela.Heblo.Application/Features/Bank/Infrastructure/BankStatementStatisticsSourceAdapter.cs` — replaced `ApplicationDbContext` field/constructor param with `IBankStatementImportRepository`; removed `using Anela.Heblo.Persistence` and `using Microsoft.EntityFrameworkCore`; replaced both LINQ blocks with a single `await _repository.GetDailyStatisticsAsync(...)` call; UTC guard and gap-fill loop unchanged
- `backend/test/Anela.Heblo.Tests/Features/Bank/BankStatementStatisticsSourceAdapterTests.cs` — added `using Anela.Heblo.Persistence.Features.Bank;`; changed adapter construction from `new BankStatementStatisticsSourceAdapter(_context)` to `new BankStatementStatisticsSourceAdapter(new BankStatementImportRepository(_context))`

## Tests

All 5 existing `BankStatementStatisticsSourceAdapterTests` pass (0 failures, 0 skipped).

```
Passed!  - Failed: 0, Passed: 5, Skipped: 0, Total: 5, Duration: 73 ms
```

## How to verify

```bash
cd /home/user/worktrees/feature-3393-Arch-Review-Bank-Bankstatementstatisticssourceadap
dotnet build Anela.Heblo.sln
dotnet format Anela.Heblo.sln --verify-no-changes
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
    --filter "FullyQualifiedName~BankStatementStatisticsSourceAdapterTests"
```

## Notes

- `dotnet format` flagged alignment spaces in the switch expression arms (`var endUnspecified   =` style). Auto-format was applied and verified clean.
- The adapter's UTC guard (`ToUniversalTime()`) is retained in the adapter layer before delegating, so the repository always receives UTC-normalised inputs. The repository then re-strips the Kind to `Unspecified` before querying EF Core (matching the original pattern).
- No architectural deviations from the task specification.

## PR Summary

Moves EF Core statistics queries from the application-layer adapter into `IBankStatementImportRepository`, so the adapter depends on the repository abstraction rather than `ApplicationDbContext` directly. This improves testability (repository can be mocked) and keeps persistence concerns in the Persistence project.

### Changes

- `IBankStatementImportRepository.cs` — new `GetDailyStatisticsAsync` contract
- `BankStatementImportRepository.cs` — implementation of the new method (switch on `dateType`, AsNoTracking GroupBy queries, UTC Kind projection)
- `BankStatementStatisticsSourceAdapter.cs` — now depends on `IBankStatementImportRepository`; EF/Persistence usings removed
- `BankStatementStatisticsSourceAdapterTests.cs` — constructor updated to wrap context in repository

## Status
DONE
