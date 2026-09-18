# Code Review: packaging-handlers

## Summary
`ScanPackingOrderHandler` and `CompletePackingOrderHandler` now consume the consumer-owned `IPackedOrderStatusUpdater` contract instead of `ShoptetOrders.IEshopOrderClient`, matching the pattern already used for `IShipmentDeliveryChecker`/`ILeafletKnowledgeSource`. The implementation deviated from the task-context's literal instruction to fully remove the `ShoptetOrders` using in `ScanPackingOrderHandler.cs`, but for a correct reason (that namespace still legitimately supplies `IPackingOrderClient`/`PackingOrder`, an unrelated dependency), and it caught and fixed an extra call site the task-context missed. Both are verified: the file still compiles and the deviation is disclosed.

## Review Result: PASS

### task: packaging-handlers
**Status:** PASS

## Docs to Update
(none — internal refactor only, no public behaviour or docs-relevant surface changed)

## Overall Notes
- Verified full solution build (`dotnet build Anela.Heblo.sln`) is clean, and the targeted test filter (ScanPackingOrderHandlerTests, CompletePackingOrderHandlerTests, ScanPackingOrderHandlerPackagePersistenceTests) passes 22/22 with 0 failures.
- The deviation from the task-context (keeping `using Anela.Heblo.Application.Features.ShoptetOrders;` in `ScanPackingOrderHandler.cs`/its test, because `IPackingOrderClient`/`PackingOrder` also live there) is correct and necessary — the spec's literal instruction would not compile. Removing it fully in `CompletePackingOrderHandler.cs`, where it truly was no longer needed, is right.
- `ScanPackingOrderHandlerPackagePersistenceTests.cs`, a call site the task-context did not enumerate, was found and fixed the same way — good catch, consistent with the rest of the swap.
- Field/parameter renames (`_eshopOrderClient` → `_packedOrderStatusUpdater`) are consistent across implementation and all three affected test files; mock `Verify`/`Setup` call sites all retargeted with identical arguments/`Times.*`.
- Remaining `IEshopOrderClient` references elsewhere in the repo (ExpeditionList handlers, ShoptetOrders' own module wiring/tests, `ModuleBoundariesTests`) are correctly left untouched — they belong to the still-pending `expeditionlist-*` and `module-boundary-enforcement` tasks.
