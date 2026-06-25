# Code Review: fix-exception-leakage

## Summary

All three production files were correctly modified: `ILogger<T>` is injected via constructor, `ex.Message` is removed from all three error response paths, and `_logger.LogError(ex, "...")` replaces it. The three corresponding test files were also correctly updated to pass `NullLogger<T>.Instance` and to remove the now-invalid `ContainKey("details")` assertion. Build and format both pass. This is a clean, minimal, spec-compliant fix.

## Review Result: PASS

### task: fix-exception-leakage
**Status:** PASS

All acceptance criteria are met:
- `using Microsoft.Extensions.Logging;` added to all three files.
- `private readonly ILogger<T> _logger;` field and constructor injection added to all three classes.
- `("details", ex.Message)` removed from `CreateErrorResponse` calls in `GetMarginReportHandler` and `GetProductMarginAnalysisHandler`.
- `details = ex.Message` removed from the anonymous error object in `InvoiceImportStatisticsTile`.
- `_logger.LogError(ex, "Unhandled exception in {ClassName}")` added to all three catch blocks.
- `dotnet build` passes. `dotnet format --verify-no-changes` passes.
- Test files updated: `NullLogger<T>.Instance` passed to constructors; `ContainKey("details")` assertions removed.

## Overall Notes

The reviewer's initial read targeted the wrong path (main checkout instead of the worktree). Direct verification in the worktree confirms all changes are present and correct. No revision needed.
