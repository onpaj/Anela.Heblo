# Implementation: add-bulk-existence-lookup-repository-method

## What was implemented

Added `GetExistingTransactionIdsAsync(string platform, IEnumerable<string> transactionIds, CancellationToken ct)` to `IImportedMarketingTransactionRepository` and implemented it on `ImportedMarketingTransactionRepository` with a single `WHERE Platform == platform && ids.Contains(TransactionId)` query that projects to `TransactionId` and returns a `HashSet<string>`. Empty input short-circuits before touching `DbSet`, mirroring `ManufacturedProductInventoryRepository.GetByProductCodesWithLogsAsync`. This method will let `MarketingInvoiceImportService.ImportAsync` (a separate, not-yet-started task: `use-bulk-lookup-in-marketing-invoice-import-service`) replace its per-transaction `ExistsAsync` DB round-trip with one bulk lookup per import run.

Followed the task-context's exact TDD steps: wrote the failing test first, confirmed it failed to compile with the expected `CS1061` (missing method), added the interface method, implemented it, then confirmed both tests pass.

## Files created/modified

- `backend/test/Anela.Heblo.Tests/Persistence/Features/MarketingInvoices/ImportedMarketingTransactionRepositoryTests.cs` — new, exactly as specified in the task context (2 tests)
- `backend/src/Anela.Heblo.Domain/Features/MarketingInvoices/IImportedMarketingTransactionRepository.cs` — added the new method signature
- `backend/src/Anela.Heblo.Persistence/Features/MarketingInvoices/ImportedMarketingTransactionRepository.cs` — added the implementation (added `using Microsoft.EntityFrameworkCore;` for `ToListAsync`)

## Tests

- `ImportedMarketingTransactionRepositoryTests.GetExistingTransactionIdsAsync_ReturnsOnlyIdsPresentForGivenPlatform` — seeds two platforms with an overlapping `TransactionId`, asserts only the queried platform's matching ids come back.
- `ImportedMarketingTransactionRepositoryTests.GetExistingTransactionIdsAsync_EmptyIdList_ReturnsEmptySet` — asserts the empty-input short-circuit returns an empty set without querying.

Both use the EF Core InMemory provider via `ApplicationDbContext`, matching the existing test's pattern in this repo.

## How to verify

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~ImportedMarketingTransactionRepositoryTests"
```

Result: `Passed! - Failed: 0, Passed: 2, Skipped: 0, Total: 2`.

## Notes

**Out-of-scope build fix bundled in a separate commit.** The solution failed to compile before any of this task's changes, with an unrelated pre-existing bug in `backend/src/Anela.Heblo.Application/Features/BackgroundJobs/Services/RecurringJobSeeder.cs`: `HasSeededFieldsChanged(existing, config)` passed the whole `List<RecurringJobConfiguration>` (`existing`) instead of the matched `existingConfig` from the preceding `TryGetValue`, producing `CS1503` and breaking `dotnet build`/`dotnet test` for the entire solution (confirmed present on `origin/main` HEAD `c2cbcc148` too — introduced by already-merged PRs #4323/#4324). Since `Anela.Heblo.Tests` transitively depends on `Anela.Heblo.Application`, this blocked compiling and running even this task's own new test. Applied the minimal one-line fix (`existing` → `existingConfig`) in its own commit (`fix(background-jobs): pass matched config, not the whole list, to HasSeededFieldsChanged`), separate from this task's feature commit, so it stays traceable and easy to review/revert independently. Flagging this for visibility since it's outside this task's stated scope.

No other deviations from the task context. `use-bulk-lookup-in-marketing-invoice-import-service` (the task that actually wires this method into `MarketingInvoiceImportService.ImportAsync`) is untouched — out of scope for this task.

## PR Summary

Adds `GetExistingTransactionIdsAsync` to `IImportedMarketingTransactionRepository` / `ImportedMarketingTransactionRepository`, a bulk existence-lookup query (`WHERE Platform == p && ids.Contains(TransactionId)`) that will replace the per-transaction `ExistsAsync` round-trip currently driving the N+1 pattern in `MarketingInvoiceImportService.ImportAsync`. Mirrors the existing `ManufacturedProductInventoryRepository.GetByProductCodesWithLogsAsync` pattern (early-return on empty input, plain `Contains`). Wiring this into `ImportAsync` itself is a separate, not-yet-started task.

Also includes an unrelated one-line fix to `RecurringJobSeeder` (in its own commit) that was needed to unblock the solution build, which was broken on `main` before this branch started.

### Changes
- `backend/src/Anela.Heblo.Domain/Features/MarketingInvoices/IImportedMarketingTransactionRepository.cs` — added `GetExistingTransactionIdsAsync` signature
- `backend/src/Anela.Heblo.Persistence/Features/MarketingInvoices/ImportedMarketingTransactionRepository.cs` — implemented `GetExistingTransactionIdsAsync`
- `backend/test/Anela.Heblo.Tests/Persistence/Features/MarketingInvoices/ImportedMarketingTransactionRepositoryTests.cs` — new repository tests
- `backend/src/Anela.Heblo.Application/Features/BackgroundJobs/Services/RecurringJobSeeder.cs` — unrelated build-fix (separate commit)

## Status
DONE
