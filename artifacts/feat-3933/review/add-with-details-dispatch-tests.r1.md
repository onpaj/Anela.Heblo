# Code Review: add-with-details-dispatch-tests

## Summary
The implementation adds exactly the two `[Fact]` tests specified in the task context,
inserted verbatim at the correct location in `GetIssuedInvoiceDetailHandlerTests`. Both
tests exercise the FR-2 dispatch behavior (`WithDetails == true` → `GetByIdWithSyncHistoryAsync`,
`WithDetails == false` → `GetByIdAsync`) with correct mock setups, `Times.Once`/`Times.Never`
verification of both repository methods, and success/mapped-response assertions. The full
test class was run and all 5 tests pass.

## Review Result: PASS

### task: add-with-details-dispatch-tests
**Status:** PASS

## Docs to Update
(none — this is a test-only change with no public behavior, API, or docs impact)

## Overall Notes
No production code changes were needed, consistent with the task context's expectation
that the handler already implements this dispatch behavior. No deviations from the
specified test code.
