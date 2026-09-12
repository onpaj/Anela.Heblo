# Specification: Remove dead `result.Failed` write in MarketingInvoiceImportService catch block

## Summary
`MarketingInvoiceImportService.ImportAsync` has a `catch` block around the final `SaveChangesAsync` call that increments `result.Failed` and then unconditionally rethrows. Because the method always rethrows, `result` is never returned to the caller on this path, so the increment is dead code that misleads readers. This is a small, surgical cleanup: delete the dead assignment (and its now-orphaned comment), no behavior change.

## Background
Arch-review finding filed against `backend/src/Anela.Heblo.Application/Features/MarketingInvoices/Services/MarketingInvoiceImportService.cs` (lines 96–111). The final persist step wraps `await _repository.SaveChangesAsync(ct)` in a try/catch. On failure it logs, sets `result.Failed += stagedCount`, then executes `throw;` unconditionally — so the mutated `result` object is discarded by the exception path and never observed by any caller. The line is confusing (the adjacent comment implies `result` will still be inspected) and carries a theoretical `OverflowException` risk on `+=` that would mask the original exception.

## Functional Requirements

### FR-1: Remove the dead `result.Failed` write
Delete the line `result.Failed += stagedCount;` and the comment line `// result.Imported intentionally stays 0 — nothing was committed.` from the `catch` block around the post-loop `SaveChangesAsync` call. Keep the existing `_logger.LogError(...)` call and the unconditional `throw;` unchanged.

**Acceptance criteria:**
- The catch block for the final `SaveChangesAsync` call no longer contains `result.Failed += stagedCount;` or the associated comment.
- The `_logger.LogError` call (message, arguments) is unchanged.
- `throw;` remains, unconditional and unchanged — behavior (exception propagation) is identical before and after.
- No other line in `MarketingInvoiceImportService.cs` is touched.

### FR-2: Preserve existing test coverage
The existing test `ImportAsync_FinalSaveChangesThrows_Rethrows` in `backend/test/Anela.Heblo.Tests/Features/MarketingInvoices/MarketingInvoiceImportServiceTests.cs` asserts that the exception propagates and that `SaveChangesAsync` was called once. It does not (and cannot, since `result` is never returned on this path) assert on `result.Failed`. This test must continue to pass unmodified after the fix.

**Acceptance criteria:**
- `ImportAsync_FinalSaveChangesThrows_Rethrows` passes without modification after the change.
- Full `MarketingInvoiceImportServiceTests` suite passes.

## Non-Functional Requirements

### NFR-1: Behavior parity
This is a pure dead-code removal. Observable behavior (return values, exceptions, logging) must be byte-for-byte identical to before the change in every code path.

### NFR-2: Risk
None beyond standard regression risk of a one-line deletion. No data model, API, or contract changes.

## Data Model
Not applicable — no schema, DTO, or contract changes. `MarketingImportResult.Failed` (the property) is untouched; only one dead write to it is removed.

## API / Interface Design
Not applicable — no public interface, endpoint, or contract change. `IMarketingInvoiceImportService.ImportAsync` signature and behavior are unchanged.

## Dependencies
None. Self-contained change to one file plus verification against existing tests.

## Out of Scope
- Any change to how per-transaction failures (inside the `foreach` loop) are counted — that `catch` block's `result.Failed++` is legitimate (it runs on a path where `result` is later returned) and is not touched.
- Any change to `MarketingImportResult`, the repository interface, or the caller (`ImportMarketingInvoicesHandler` / equivalent).
- Any refactor of the surrounding method beyond the two deleted lines.

## Open Questions
None.

## Status: COMPLETE
