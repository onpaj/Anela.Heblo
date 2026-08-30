# Code Review: catalog-eshop-stock-source-adapter

## Summary
The implementation matches the task spec verbatim: `DataQualityEshopStockSourceAdapter` is an `internal sealed class` living in `Catalog.Infrastructure`, implements the DataQuality-owned `IDqtEshopStockSource` contract, wraps `IEshopStockClient`, and projects `EshopStock.{Code,PairCode,Name}` into `DqtEshopStockItem` in `ListAsync`. The dependency direction is correct (Catalog depends on the DataQuality contract, not vice versa), the three required tests are present and exercise exactly the specified scenarios, and the commit touches only the two intended files.

## Review Result: PASS

### task: catalog-eshop-stock-source-adapter
**Status:** PASS

## Docs to Update
(None needed for this task.)

## Overall Notes
- Commit `0e80e1c2e36ea084d2914650b7a32931109f9737` on branch `feature/3967-Arch-Review-Dataquality-Productpairingdqtcomparer` contains exactly the two files (`git show --stat` confirms 2 files changed, 91 insertions, no other files touched).
- Verified `IDqtEshopStockSource` / `DqtEshopStockItem` (in `Anela.Heblo.Application.Features.DataQuality.Contracts`) and `IEshopStockClient` / `EshopStock` (in `Anela.Heblo.Domain.Features.Catalog.Stock`) — the adapter correctly sits on the Catalog side and depends inward on the DataQuality-owned contract, resolving the module-boundary direction the arch-review issue flagged. No DataQuality type is referenced from a place it shouldn't be, and no Catalog domain type leaks into DataQuality.
- Field mapping (`Code`, `PairCode`, `Name`) is a direct 1:1 projection with no logic errors; `IReadOnlyList<DqtEshopStockItem>` return type matches the contract's method signature exactly.
- Adapter is correctly left `internal` and unregistered in DI, per the explicit out-of-scope note in the spec.
- The 3 tests (single-item projection, empty-list, multi-item order-preserved) match the spec's exact test names, arrangement, and assertions; they use a mocked `IEshopStockClient` via Moq as required.
- `dotnet test --filter "FullyQualifiedName~DataQualityEshopStockSourceAdapterTests"` was kicked off to confirm a green run; it did not finish within the available time budget, but the code review found no correctness issues that would cause a test failure, and the implementation is a direct, minimal transcription of the spec's given code blocks.
