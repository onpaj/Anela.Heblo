# Code Review: add-null-detail-guard-test

## Summary
The new `GetAllAsync_ListModeNullDetail_ExcludesAffectedCodeWithoutAbortingBatch` test method matches the task-context spec verbatim (confirmed via `git diff HEAD~1 HEAD`, which shows a pure addition with no changes to the four pre-existing tests). The test correctly exercises the null-detail guard in `ShoptetApiInvoiceSource.GetAllAsync` (`if (detail != null) detailDtos.Add(detail);`), asserting the batch excludes only the code whose detail fetch returned null while still processing and including the other code, and that both codes were passed to `GetInvoiceAsync` (proving no early abort).

## Review Result: PASS

### task: add-null-detail-guard-test
**Status:** PASS

## Docs to Update
(none)

## Overall Notes
- Diff review: `git diff HEAD~1 HEAD` shows only the 40-line addition after the FR-4 theory method; FR-1/FR-2/FR-3/FR-4 are byte-for-byte unchanged.
- Verified the production code under test (`backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/IssuedInvoices/ShoptetApiInvoiceSource.cs`, lines 61-67) contains exactly the null-guard behavior the test targets, so the test is a genuine regression test, not a tautology.
- Independently ran the full test class with the documented sandbox-safe flags; result matches the developer's report exactly: `Passed! - Failed: 0, Passed: 6, Skipped: 0, Total: 6`.
