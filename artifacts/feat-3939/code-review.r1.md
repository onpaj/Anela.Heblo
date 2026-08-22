## Review Result: CLEAN

### Blocking (correctness)
- None

### Advisory (cleanup)
- None

## Notes
Reviewed the full feature-branch diff (`origin/main`...HEAD, merge-base `e03bd604f4d00d99aad8eb4dd782b8aa07e92deb`) against `spec.r1.md`. The only production/test code change is the new file `backend/test/Anela.Heblo.Adapters.Shoptet.Tests/Unit/ShoptetApiInvoiceSourceTests.cs` (198 lines); everything else in the diff is pipeline artifact bookkeeping under `artifacts/feat-3939/`. No production source file is touched, matching the spec's "test-only change" framing.

Verified by reading `ShoptetApiInvoiceSource.cs`, `ShoptetInvoiceMapper.cs`, `IShoptetInvoiceClient.cs`, `IssuedInvoiceSourceQuery.cs`, and `ShoptetInvoiceDto.cs` and tracing each of the 6 test cases against the real implementation:

- FR-1 (`GetAllAsync_SingleInvoiceModeFound_...`): correctly asserts `ListInvoicesAsync` is never called and `GetInvoiceAsync` is called once with the exact `InvoiceId`; the `OrderCode` assertion correctly accounts for `ShoptetInvoiceMapper.Map` swapping `Code`/`OrderCode` (`mapped.OrderCode = src.Code`), so it genuinely proves the real mapper ran rather than being a tautology.
- FR-2 (`...NotFound_...`): correctly covers the null-return branch, asserting a non-null empty `Invoices` list rather than a null list or thrown exception.
- FR-3 (`...ExcludesNonMatchingCurrency`): correctly arranges mixed-currency summaries and asserts `GetInvoiceAsync` is called only for the matching code.
- FR-4 (`...IsCaseInsensitive`, `[Theory]` with both casing directions): correctly proves the filter's `StringComparison.OrdinalIgnoreCase` behavior from both directions.
- FR-5 (`...ExcludesAffectedCodeWithoutAbortingBatch`): correctly proves the null-detail guard doesn't short-circuit the loop, by asserting both codes were passed to `GetInvoiceAsync` while only the non-null one appears in the result.

`BuildDto` initializes `Items` to a non-null empty list and `Price` to a non-null DTO, avoiding the `NullReferenceException` the spec calls out as a hazard in `ShoptetInvoiceMapper.Map`.

Independently re-ran the new tests (`dotnet test ... -c Release --filter "FullyQualifiedName~Unit.ShoptetApiInvoiceSourceTests"`): `Passed! - Failed: 0, Passed: 6, Skipped: 0, Total: 6`. (Release configuration used per this branch's own developer/reviewer notes: Debug build intermittently stalls in the API project's `GenerateAccessMatrix` pre-build target in this sandbox — an unrelated, pre-existing environment issue.)

No deviations from the spec, no missing FR coverage, no correctness issues, no reuse/simplification/efficiency concerns worth flagging in a 198-line self-contained test file that already follows the sibling `ShoptetApiExpeditionListSourceTests` pattern as directed by the spec.
