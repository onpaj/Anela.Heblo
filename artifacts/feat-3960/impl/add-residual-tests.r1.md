# add-residual-tests r1

Added two tests to `backend/test/Anela.Heblo.Adapters.Shoptet.Tests/Unit/ShoptetApiInvoiceSourceTests.cs`:
- `GetAllAsync_ListModeNullPrice_ExcludesItemWithoutFetchingDetail` (list item with `Price = null` filtered out, no detail call)
- `GetAllAsync_ListModeEmptyList_ReturnsSingleEmptyBatchWithoutDetailCalls` (BatchId == RequestId, empty invoices, no `GetInvoiceAsync` calls)

## Results
- `dotnet test -c Release --filter ShoptetApiInvoiceSourceTests`: Passed 8, Failed 0.
- `dotnet format` on the test file: no changes.
- Test-only change; no production code touched.
