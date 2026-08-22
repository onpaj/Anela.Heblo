# Code Review: add-currency-filter-exclude-test

## Summary
The implementation replaces `ShoptetApiInvoiceSourceTests.cs` with exactly the content specified in the task context, adding one new `[Fact]` (`GetAllAsync_ListModeCurrencyFilter_ExcludesNonMatchingCurrency`) covering FR-3 (list-mode currency filter excludes a non-matching-currency summary from both the detail-fetch calls and the result), while leaving the pre-existing FR-1/FR-2 tests intact. The resulting file was diffed against the task context's exact specified content and is byte-for-byte identical. `dotnet test` confirms the new test passes individually and the full class passes 3/3.

## Review Result: PASS

### task: add-currency-filter-exclude-test
**Status:** PASS

## Docs to Update
(None — this is test-only coverage for existing production code; no public behavior, CLI, or docs changed.)

## Overall Notes
- Verified the new test's assertions against the real `ShoptetApiInvoiceSource.GetAllAsync` list-mode branch: it filters `listItems` by `Price.CurrencyCode` (case-insensitive) equal to `query.Currency` before fetching details, matching the test's setup (dtoA=CZK matches query Currency="CZK", dtoB=EUR excluded) and its assertions (`GetInvoiceAsync("A")` called once, `GetInvoiceAsync("B")` never called, result contains only the mapped "A" invoice).
- Verified `IShoptetInvoiceClient.ListInvoicesAsync`/`GetInvoiceAsync` signatures and `IssuedInvoiceSourceQuery.Currency` match what the test uses — compiles and runs against the real types, no test doubles diverging from production contracts.
- Commit message matches the task context's specified message exactly (`test: add ShoptetApiInvoiceSource currency-filter-excludes coverage (FR-3)`).
- The impl artifact documents a sandbox-specific `dotnet test` deadlock (MSBuild Server / nested `dotnet run` inside the `GenerateAccessMatrix` Debug-only build target) and its workaround (`DOTNET_CLI_USE_MSBUILDSERVER=false`, `MSBUILDDISABLENODEREUSE=1`, `-p:UseSharedCompilation=false`). This is an environment/tooling observation, not a code change, and does not affect this review — worth carrying into project memory for future runs but out of scope for this task's acceptance criteria.
- No regressions: full `ShoptetApiInvoiceSourceTests` class run reports `Passed! - Failed: 0, Passed: 3, Skipped: 0, Total: 3`.

**Status:** PASS
