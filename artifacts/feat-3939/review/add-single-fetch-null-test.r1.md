# Code Review: add-single-fetch-null-test

## Summary
The implementation adds exactly the FR-2 test method specified in the task, appended after the untouched FR-1 test, with no other changes to the file. The new test is a genuine regression test: it fails with a `NullReferenceException` if the production null-guard in `ShoptetApiInvoiceSource.GetAllAsync` (`single != null ? new[] { single } : Array.Empty<ShoptetInvoiceDto>()`) were removed, since `ShoptetInvoiceMapper.Map` dereferences `src.Items` unconditionally. Independent `dotnet test` run confirms both tests pass.

## Review Result: PASS

### task: add-single-fetch-null-test
**Status:** PASS

## Overall Notes
- Diff verified via `git log -p`: the commit is a pure append (new `[Fact]` method only), matching the task spec's code verbatim, character-for-character. FR-1 test and shared helpers (`BuildMapper`, `BuildSource`, `BuildDto`) are untouched by this task's commit.
- Traced the null path through production code: `query.QueryByInvoice` → `GetInvoiceAsync` returns `null` → `single != null` is false → `Array.Empty<ShoptetInvoiceDto>()` → `.Select(...).ToList()` on an empty array yields `[]` → batch has `Invoices = []` (non-null, empty). The test's assertions (`BatchId == "REQ-2"`, `Invoices` non-null and empty, no exception) match this path exactly.
- Confirmed the test is not vacuously true: if the null-guard were removed and `single` (null) were passed directly into `_mapper.Map`, `ShoptetInvoiceMapper.Map` would throw `NullReferenceException` on `src.Items` — the test would then fail (unhandled exception), so it does catch regression of the guard.
- Independent verification: `dotnet test ... --filter "FullyQualifiedName~ShoptetApiInvoiceSourceTests"` → `Passed! - Failed: 0, Passed: 2, Skipped: 0, Total: 2`.
