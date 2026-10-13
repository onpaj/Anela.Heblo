## Module / File
`backend/src/Adapters/Anela.Heblo.Adapters.Flexi/Bank/FlexiBankStatementImportService.cs`

## Coverage
Line coverage: 29.2% (filter threshold: 60%)

## What's not tested
`ImportStatementAsync` has three outcome paths and likely only the success path is lightly covered:

1. **Explicit failure from FlexiBee** (lines 34–38): when `flexiResult.IsSuccess` is false, the method returns `Result.Failure<bool>(flexiResult.ErrorMessage ?? "Unknown import error")`. No test asserts the returned failure result or the fallback "Unknown import error" message when `ErrorMessage` is null.
2. **Exception path** (lines 42–44): when `_flexiBankAccountClient.ImportStatementAsync` throws, the catch block returns `Result.Failure<bool>($"Exception during import: {ex.Message}")`. No test verifies this branch is taken or that the exception message is included in the failure.
3. **Success path** (lines 28–32): while partially covered by existing code paths, there is no dedicated assertion that `Result.Success(true)` is returned on a successful FlexiBee call.

## Why it matters
Bank statement import is financial infrastructure. If the failure branch accidentally returns `Result.Success` instead of `Result.Failure` (e.g., after a refactor), callers would treat a failed import as succeeded, causing missing bank entries to go unnoticed. The catch block catching all exceptions and converting them to `Result.Failure` is also an important contract — a regression here would surface as an unhandled exception propagating up.

## Suggested approach
Unit tests with a mocked `FlexiBankAccountClient`:
- Client returns success → `Result.Success(true)` with log at Information level
- Client returns failure with an error message → `Result.Failure<bool>` containing that message
- Client returns failure with null error message → `Result.Failure<bool>` containing "Unknown import error"
- Client throws → `Result.Failure<bool>` containing the exception message, no rethrow

Effort: ~1 hour.

---
_Filed by weekly coverage-gap routine on 2026-09-28. Based on CI run #35977921040 (22bb3b8ff6194bdd055cb438d08b7c6633a85221)._
