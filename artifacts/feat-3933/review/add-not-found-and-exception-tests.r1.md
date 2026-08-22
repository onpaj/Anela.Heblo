# Code Review: add-not-found-and-exception-tests

## Summary
The implementation adds exactly the two `[Fact]` tests specified in the task context,
inserted verbatim at the correct location in `GetIssuedInvoiceDetailHandlerTests`. Both
tests exercise the FR-3 (not-found → `ErrorCodes.ResourceNotFound`, mapper never invoked)
and FR-4 (repository throws → caught, `ErrorCodes.Exception`, no rethrow) error paths with
correct mock setups and assertions matching the exact expected error codes and messages.
The full test class was run and all 7 tests pass; the solution builds with 0 errors and
`dotnet format --verify-no-changes` reports no violations in the modified file.

## Review Result: PASS

### task: add-not-found-and-exception-tests
**Status:** PASS

## Docs to Update
(none — this is a test-only change with no public behavior, API, or docs impact)

## Overall Notes
No production code changes were needed, consistent with the task context's expectation
that the handler already implements this error-handling behavior. No deviations from the
specified test code.
