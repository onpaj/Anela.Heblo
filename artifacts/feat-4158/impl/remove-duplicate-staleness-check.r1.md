# Implementation: remove-duplicate-staleness-check

## What was implemented

Removed the duplicate staleness-warning check from `ImportBankStatementHandler.Handle`. `BankImportJobBase.ResolveDateFromAsync` remains the sole source of the "watermark is N days stale" warning for scheduled job runs, eliminating the double-log per job run described in issue #4158. The manual-API trigger path (`BankStatementsController`) no longer logs a staleness warning at all — an accepted, documented trade-off per `arch-review.r1.md` Decision 2.

## Files created/modified

- `backend/src/Anela.Heblo.Application/Features/Bank/UseCases/ImportBankStatement/ImportBankStatementHandler.cs` — removed the `using Anela.Heblo.Application.Features.Bank.Infrastructure.Jobs;` directive, the `_watermarkOptions` field, the `IOptions<BankImportWatermarkOptions> watermarkOptions` constructor parameter and its assignment, and the `if (state.LastValidImportDate.HasValue) { ... LogWarning(...) }` block in `Handle`. The `state` load itself is untouched — it's still needed for `RecordSuccess`/`RecordFailure`/`UpsertAsync`.
- `backend/test/Anela.Heblo.Tests/Features/Bank/ImportBankStatementHandlerTests.cs` — dropped the now-removed `Options.Create(new BankImportWatermarkOptions())` argument from both `ImportBankStatementHandler` constructor call sites; deleted `Handle_LogsStaleWarning_WhenWatermarkIsStale` and `Handle_DoesNotLogWarning_WhenWatermarkIsFresh`; added `Handle_DoesNotLogStaleWarning_EvenWhenWatermarkIsStale` (regression test proving the handler no longer logs the warning even with a 10-day-stale watermark).

## Tests

- `ImportBankStatementHandlerTests.Handle_DoesNotLogStaleWarning_EvenWhenWatermarkIsStale` — new regression test for issue #4158; asserts `Times.Never` for the "stale" warning log even when `LastValidImportDate` is 10 days old.
- All other existing tests in `ImportBankStatementHandlerTests` and `BankImportJobBaseTests` — unchanged, still pass (job-side warning logic untouched).

## How to verify

1. `cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~ImportBankStatementHandlerTests|FullyQualifiedName~BankImportJobBaseTests"` → confirmed: 22/22 passed.
2. `dotnet build Anela.Heblo.sln` (from repo root) → confirmed: Build succeeded, 0 errors, no new warnings referencing `ImportBankStatementHandler`.
3. `dotnet format Anela.Heblo.sln --verify-no-changes` (from repo root) → confirmed: exit 0, no formatting diffs.
4. `cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~Features.Bank"` → 118 passed, 8 failed. All 8 failures are in `BankStatementImportRepositoryIntegrationTests` and fail with `System.ArgumentException: Docker is either not running or misconfigured` (Testcontainers/Postgres) — a pre-existing sandbox limitation (`docker ps` confirms no Docker daemon is reachable here), unrelated to this change and not touching any file this task modified.

## Notes

- Followed the task-context's exact plan (test-first: Step 1 test edits, Step 2 confirmed compile failure with the expected `CS7036` error at both call sites, Step 3 production edit, Step 4 green).
- No deviations from the task-context or arch-review.

## PR Summary
Removed the duplicate staleness-warning log from `ImportBankStatementHandler.Handle` (issue #4158). `BankImportJobBase` was already logging this warning before invoking the handler on every scheduled job run, so the handler's independent re-check caused the same warning to be logged twice per run. The handler's job is to execute an import for a given date range, not to judge whether that range is "stale" — that orchestration-level decision belongs solely to `BankImportJobBase`, which is unchanged. The manual-API trigger path (`BankStatementsController`) no longer logs a staleness warning; this is an accepted, minimal-footprint trade-off (see `arch-review.r1.md` Decision 2) rather than a regression to fix here.

### Changes
- `backend/src/Anela.Heblo.Application/Features/Bank/UseCases/ImportBankStatement/ImportBankStatementHandler.cs` — removed the duplicate staleness check, the now-unused `_watermarkOptions` field/constructor param, and the unused `Infrastructure.Jobs` using directive
- `backend/test/Anela.Heblo.Tests/Features/Bank/ImportBankStatementHandlerTests.cs` — updated constructor call sites to the new 7-arg signature; replaced the two stale-warning tests with a single regression test asserting the handler never logs the warning

## Status
DONE
