## Module
ExpeditionList (Shoptet adapter)

## Finding
`ShoptetApiExpeditionListSource.CreatePickingList` in `backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Expedition/ShoptetApiExpeditionListSource.cs` (lines 51–234) is 183 lines.

It contains a nested local function `FlushBatchAsync` (lines 126–196, ~70 lines) that closes over eight outer-scope variables: `_catalog`, `_client`, `batchIndex`, `method`, `carrierDisplayName`, `exportedFiles`, `cancellationToken`, and `onBatchFilesReady`. The local function packs four distinct responsibilities:
1. Catalog enrichment (stock counts, warehouse positions, product cooling)
2. PDF byte generation via `_generateDocument`
3. Shoptet API side-effect: writing the cooling marker via `SetAdditionalFieldAsync`
4. Invoking the `onBatchFilesReady` batch callback

Because `FlushBatchAsync` is a local function — not a private method — it cannot be targeted by a unit test directly. Existing tests exercise it only through full `CreatePickingList` integration paths, making it impossible to test the cooling-marker write in isolation without running the entire list-creation flow.

## Why it matters
Exceeds the 50-line method guideline. The deep outer-variable closure makes the control flow hard to follow and incremental changes risky: modifying any captured variable's type or lifetime can have non-obvious effects on `FlushBatchAsync`'s behaviour. The cooling-marker Shoptet API call (a live-store write) buried inside PDF generation is particularly hard to mock out.

## Suggested fix
Introduce a `PickingListBatchProcessor` private helper (or a separate class if complexity warrants it) whose `FlushAsync(List<ExpeditionOrder> batch, ...)` method receives all needed context as explicit parameters rather than captured outer variables. This makes each responsibility independently testable and reduces `CreatePickingList` to a loop-and-dispatch driver well under 50 lines.

---
_Filed by daily arch-review routine on 2026-06-06._