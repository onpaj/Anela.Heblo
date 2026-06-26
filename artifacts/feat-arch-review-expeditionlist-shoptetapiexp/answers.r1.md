### Question 1
OQ-1: Logger type for the new helper. The helper logs only one message (the PATCH warning). Two options:
- (a) Reuse the existing `ILogger<ShoptetApiExpeditionListSource>` passed through as `ILogger`. Keeps log category stable for ops/alerts.
- (b) Give the helper its own `ILogger<PickingListBatchProcessor>`. Cleaner DI semantics, but changes the log category and would surprise anyone filtering on the existing category.

**Answer:** Go with option (a). The `PickingListBatchProcessor` constructor takes a plain `ILogger` parameter, and `ShoptetApiExpeditionListSource` passes its existing `ILogger<ShoptetApiExpeditionListSource>` instance through. Do not introduce `ILogger<PickingListBatchProcessor>` and do not register the helper in DI.

**Rationale:** The cooling-marker warning log is the operational signal ops/alerting filter on by the `ShoptetApiExpeditionListSource` category; option (b) would silently change the category and break any external filters. It also keeps the existing `CreatePickingList_PatchFails_PdfStillCompletes` assertion against `Mock<ILogger<ShoptetApiExpeditionListSource>>` (verified at `backend/test/Anela.Heblo.Adapters.Shoptet.Tests/Expedition/ShoptetApiExpeditionListSource_CoolingMarkerTests.cs:266` and `:278`) passing unchanged, which FR-1 and FR-4 explicitly require. The helper is constructed internally inside `ShoptetApiExpeditionListSource` (NFR-3), so no DI churn is needed and the public surface stays identical.
