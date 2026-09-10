# Code Review: route-invoice-dqt-comparer-through-dqt-contracts

## Summary
The implementation successfully routes DataQuality's invoice comparison logic through consumer-owned snapshot types, cleanly decoupling the DataQuality module from Invoices-domain types. All interfaces, adapters, and consumer code have been updated correctly, with mapping logic properly placed in the Invoices module. The architectural boundary is clean and all tests (15 existing comparer tests + 5 new adapter tests) pass.

## Review Result: PASS

### task: route-invoice-dqt-comparer-through-dqt-contracts
**Status:** PASS

## Detailed Verification

### Interface Contracts ✓
- `IInvoiceShoptetSource.GetAllAsync()` now returns `List<DqtInvoiceSnapshot>` (was `List<IssuedInvoiceDetailBatch>`)
- `IInvoiceErpClient.GetAllAsync()` now returns `List<DqtInvoiceSnapshot>` (was `List<IssuedInvoiceDetail>`)
- Both interfaces have zero imports of `Anela.Heblo.Domain.Features.Invoices`
- `DqtInvoiceSourceQuery` parameter used correctly with DateOnly fields for date range

### Consumer (InvoiceDqtComparer) ✓
- Uses only DataQuality-owned types: `DqtInvoiceSnapshot`, `DqtInvoiceItem`, `DqtInvoiceSourceQuery`
- No imports of Invoices-domain types; only imports DataQuality contracts and domain
- Flat price fields (`.TotalWithVat`, `.TotalWithoutVat`, `.WithVat`, `.WithoutVat`) used directly in comparisons
- Logic unchanged: tolerance-based diffing, duplicate detection, grouping behavior preserved
- `SelectMany()` batch flattening and `ToDateTime()` conversion removed (now in adapter)

### Adapters (Provider-side Mapping) ✓
- **InvoiceShoptetSourceAdapter:**
  - Maps `DqtInvoiceSourceQuery` to `IssuedInvoiceSourceQuery` with proper field mapping
  - Converts `DateOnly` fields to `DateTime(TimeOnly.MinValue)` correctly
  - Flattens batches via `SelectMany(b => b.Invoices)`
  - Maps each invoice via `ToDqtSnapshot()` extension method
  
- **InvoiceErpClientAdapter:**
  - Forwards `from`, `to`, and cancellation token to inner client
  - Maps each invoice via `ToDqtSnapshot()` extension method

- **InvoiceDqtSnapshotMapper:**
  - Extension methods `ToDqtSnapshot()` and `ToDqtItem()` correctly flatten price objects
  - Used by both adapters to avoid duplication
  - Properly located in Invoices module (provider-owned)

### Module Boundary ✓
- Zero references to `Anela.Heblo.Domain.Features.Invoices` in `Application/Features/DataQuality` production code
- Adapters in Invoices.Infrastructure serve as clean provider-side boundary adapters
- No bidirectional dependencies; DataQuality depends only on contracts

### Test Coverage ✓
- **InvoiceDqtComparerTests:** All 15 existing tests preserved and passing
  - Test names: `BothEmpty_ReturnsZeroCheckedZeroMismatches`, `InvoiceInShoptetOnly_FlagsMissingInFlexi`, `InvoiceInFlexiOnly_FlagsMissingInShoptet`, `MatchingInvoices_ReturnsZeroMismatches`, `WithinTolerance_NoMismatch`, `RoundingDifferenceUnderHalfCrown_NoMismatch`, `WithVatDiffers_FlagsTotalWithVatDiffers`, `WithoutVatDiffers_FlagsTotalWithoutVatDiffers`, `ItemsDiffer_ByProductCode`, `ItemsDiffer_ByAmount`, `ItemPriceDiffers`, `MultipleIssues_CombinesFlags`, `DuplicateShoptetInvoiceCode_DoesNotThrow_AndFlagsDuplicate`, `DuplicateFlexiInvoiceCode_DoesNotThrow_AndFlagsDuplicate`, `DuplicateItemCodeWithinInvoice_DoesNotThrow_AndReportsDuplicate`
  - Fixtures updated to use `DqtInvoiceSnapshot` and `DqtInvoiceItem`
  - All assertions preserved and passing

- **InvoiceShoptetSourceAdapterTests (new):** 2 tests
  - `GetAllAsync_MapsQueryFieldsIntoInnerQuery` — validates query field mapping including DateOnly→DateTime conversion and default preservation
  - `GetAllAsync_FlattensBatchesAndMapsInvoicesAndItemsToSnapshots` — validates batch flattening and snapshot mapping across multiple batches

- **InvoiceErpClientAdapterTests (new):** 3 tests
  - `GetAllAsync_ForwardsFromToAndCancellationTokenToInnerClient` — validates parameter forwarding
  - `GetAllAsync_MapsInvoicesWithMultipleItemsToSnapshots` — validates invoice and multi-item mapping
  - `GetAllAsync_ReturnsEmptyList_WhenInnerResultIsEmpty` — validates empty result handling

### Build & Test Results ✓
- `dotnet build`: 0 errors (pre-existing warnings only)
- InvoiceDqtComparerTests: 15/15 PASSED
- InvoiceShoptetSourceAdapterTests: 2/2 PASSED
- InvoiceErpClientAdapterTests: 3/3 PASSED
- DataQuality + Invoices features overall: 178/180 PASSED
- 2 failures in `IssuedInvoiceRepositoryGetSyncStatsSqlShapeTests` (Docker/Testcontainers environment, file unmodified by this commit, pre-existing limitation)

### Files Modified/Created ✓
- Modified: `backend/src/Anela.Heblo.Application/Features/DataQuality/Contracts/IInvoiceShoptetSource.cs`
- Modified: `backend/src/Anela.Heblo.Application/Features/DataQuality/Contracts/IInvoiceErpClient.cs`
- Modified: `backend/src/Anela.Heblo.Application/Features/DataQuality/Services/InvoiceDqtComparer.cs`
- Modified: `backend/src/Anela.Heblo.Application/Features/Invoices/Infrastructure/InvoiceShoptetSourceAdapter.cs`
- Modified: `backend/src/Anela.Heblo.Application/Features/Invoices/Infrastructure/InvoiceErpClientAdapter.cs`
- Modified: `backend/test/Anela.Heblo.Tests/Features/DataQuality/InvoiceDqtComparerTests.cs`
- Created: `backend/test/Anela.Heblo.Tests/Features/Invoices/Infrastructure/InvoiceShoptetSourceAdapterTests.cs`
- Created: `backend/test/Anela.Heblo.Tests/Features/Invoices/Infrastructure/InvoiceErpClientAdapterTests.cs`

## Overall Notes

- **Architecture Decision:** Placing mapping logic in the Invoices module (via InvoiceDqtSnapshotMapper and adapters) is the correct pattern for this cross-boundary contract. DataQuality defines contracts but owns no production implementations; Invoices provides the adapters and mappers.

- **Note on Test Count:** Task spec referenced "14 existing tests" but the actual file has 15. The developer correctly identified this as a pre-existing off-by-one in the task prose. All 15 tests are preserved and passing.

- **DateOnly Handling:** The conversion from `DateOnly` (in `DqtInvoiceSourceQuery`) to `DateTime(TimeOnly.MinValue)` at the adapter boundary is the correct place for this transformation—it isolates the conversion to provider code and keeps the consumer clean.

- **Default Preservation:** `IssuedInvoiceSourceQuery` defaults (`InvoiceId=null`, `Currency="CZK"`) are preserved automatically without explicit assignment in the adapter, as intended.

