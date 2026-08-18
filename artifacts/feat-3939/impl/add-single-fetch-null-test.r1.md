# Implementation: add-single-fetch-null-test

## What was implemented
Added a new xUnit test, `GetAllAsync_SingleInvoiceModeNotFound_ReturnsBatchWithEmptyInvoiceList`, to `ShoptetApiInvoiceSourceTests`, covering FR-2: single-invoice mode (`IssuedInvoiceSourceQuery.InvoiceId` set) when `IShoptetInvoiceClient.GetInvoiceAsync` returns `null` for the requested id. The test asserts `ShoptetApiInvoiceSource.GetAllAsync` does not throw, returns exactly one `IssuedInvoiceDetailBatch` with the correct `BatchId`, and that `Invoices` is a non-null, empty list — matching the production code path in `ShoptetApiInvoiceSource.GetAllAsync` (`single == null` → `Array.Empty<ShoptetInvoiceDto>()` → mapped to an empty `details` list).

The pre-existing FR-1 test (`GetAllAsync_SingleInvoiceModeFound_ReturnsSingleBatchWithMappedInvoice`) and all shared test helpers (`BuildMapper`, `BuildSource`, `BuildDto`) were left unchanged. The real production types (`ShoptetApiInvoiceSource`, `IShoptetInvoiceClient`, `IssuedInvoiceSourceQuery`, `ShoptetInvoiceDto`, `ShoptetInvoiceMapper`) were verified against the actual source before writing the test; no adaptation was needed — the task's suggested code already matched the real signatures exactly (constructor order, property names, nullability of `GetInvoiceAsync`, etc.), so the file was edited in place by appending the new `[Fact]` rather than a full-file replace.

## Files created/modified
- `backend/test/Anela.Heblo.Adapters.Shoptet.Tests/Unit/ShoptetApiInvoiceSourceTests.cs` — added the FR-2 `[Fact]` test method after the existing FR-1 test; no other changes.

## Tests
- `backend/test/Anela.Heblo.Adapters.Shoptet.Tests/Unit/ShoptetApiInvoiceSourceTests.cs`
  - `GetAllAsync_SingleInvoiceModeFound_ReturnsSingleBatchWithMappedInvoice` (pre-existing, FR-1): single-invoice mode, client returns a DTO, mapper runs, batch has one mapped invoice.
  - `GetAllAsync_SingleInvoiceModeNotFound_ReturnsBatchWithEmptyInvoiceList` (new, FR-2): single-invoice mode, client returns `null`, batch has zero invoices and `Invoices` is non-null.

## How to verify
```bash
dotnet test backend/test/Anela.Heblo.Adapters.Shoptet.Tests/Anela.Heblo.Adapters.Shoptet.Tests.csproj --filter "FullyQualifiedName~ShoptetApiInvoiceSourceTests.GetAllAsync_SingleInvoiceModeNotFound_ReturnsBatchWithEmptyInvoiceList"
# Passed! - Failed: 0, Passed: 1, Skipped: 0

dotnet test backend/test/Anela.Heblo.Adapters.Shoptet.Tests/Anela.Heblo.Adapters.Shoptet.Tests.csproj --filter "FullyQualifiedName~ShoptetApiInvoiceSourceTests"
# Passed! - Failed: 0, Passed: 2, Skipped: 0
```

## Notes
No deviations from the task spec were required — the given test code compiled and passed as-is against the real production types. Both dotnet test runs took several minutes due to full solution rebuild (including access-matrix code generation as part of the Anela.Heblo.API build) rather than any test flakiness.

## Status
DONE
