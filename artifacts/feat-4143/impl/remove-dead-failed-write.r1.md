# Implementation: remove-dead-failed-write

## What was implemented
Removed the dead `result.Failed += stagedCount;` write and its now-orphaned comment
(`// result.Imported intentionally stays 0 — nothing was committed.`) from the `catch`
block wrapping the post-loop `SaveChangesAsync` call in
`MarketingInvoiceImportService.ImportAsync`.

That `catch` block ends in an unconditional `throw;`, so `result` is never returned to
any caller on this path — the increment was unobservable dead code that misled readers
(and carried a theoretical `OverflowException` on `+=` that could mask the original
exception). The `_logger.LogError(...)` call and the `throw;` are untouched,
character-for-character. No other line in the file changed.

The per-transaction `catch` inside the `foreach` loop (`result.Failed++`) is legitimate —
that path does return `result` — and was deliberately left alone, per the spec's
Out of Scope section.

## Files created/modified
- `backend/src/Anela.Heblo.Application/Features/MarketingInvoices/Services/MarketingInvoiceImportService.cs` — deleted two lines (the dead assignment and its comment) from the post-loop flush `catch` block.

## Tests
No test changes. Existing coverage verifies the behavior is unchanged:
- `backend/test/Anela.Heblo.Tests/Features/MarketingInvoices/MarketingInvoiceImportServiceTests.cs`
  - `ImportAsync_FinalSaveChangesThrows_Rethrows` — asserts the exception propagates and `SaveChangesAsync` ran once; never reads `result.Failed` (it cannot, since `result` is not returned on this path).
  - `ImportAsync_FinalSaveChangesThrows_ExceptionTypeIsPreserved` — same path, asserts exception type preservation.
  - The `result.Failed` assertions elsewhere in the file all exercise the in-loop per-transaction failure path, which is untouched.
- `backend/test/Anela.Heblo.Tests/Features/MarketingInvoices/ImportMarketingInvoicesHandlerTests.cs` — handler-level coverage, unaffected.

## How to verify
```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~MarketingInvoices" -v minimal
dotnet build Anela.Heblo.sln
dotnet format Anela.Heblo.sln --verify-no-changes
```
Result: `Passed! - Failed: 0, Passed: 14, Skipped: 0, Total: 14`.

## Notes
- The task context's Steps 3-4 said to run `dotnet build` / `dotnet format` from `backend/`.
  There is no solution or project file in `backend/` — `Anela.Heblo.sln` is at the repo
  root — so those two commands were run from the repo root instead. The test command's
  explicit `--project`-style csproj path worked as written from `backend/`.
- The change is pure dead-code removal; observable behavior (return values, exceptions,
  logging) is identical before and after.

## PR Summary
Removed a dead `result.Failed += stagedCount;` write (and its orphaned explanatory comment) from the `catch` block around the final `SaveChangesAsync` flush in `MarketingInvoiceImportService.ImportAsync`.

That catch block rethrows unconditionally, so the mutated `result` is discarded by the exception path and never reaches a caller. The assignment was therefore unobservable — but the adjacent comment implied `result` would still be inspected, which is actively misleading to a reader, and `+=` on a counter in an exception handler carries a theoretical `OverflowException` that would mask the original failure.

No behavior change: the `_logger.LogError` call and the `throw;` are untouched, and the legitimate per-transaction `result.Failed++` inside the import loop is unchanged. All 14 existing MarketingInvoices tests pass without modification.

### Changes
- `backend/src/Anela.Heblo.Application/Features/MarketingInvoices/Services/MarketingInvoiceImportService.cs` — deleted the dead `result.Failed += stagedCount;` assignment and its orphaned comment from the post-loop flush `catch` block

## Status
DONE
