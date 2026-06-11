## Module
ExpeditionList (found via Logistics integration chain)

## Finding
`PrintPickingListResult.OrderIds` in `backend/src/Anela.Heblo.Application/Features/Logistics/Picking/PrintPickingListResult.cs` (line 7) is declared and initialized to an empty list, but is never written to or read in any production code path:

- **Producer** — `ShoptetApiExpeditionListSource.CreatePickingList` (`backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Expedition/ShoptetApiExpeditionListSource.cs`, line 229) returns `new PrintPickingListResult { ExportedFiles = ..., TotalCount = ... }` and never sets `OrderIds`.
- **Consumer** — `LogisticsExpeditionPickingAdapter.CreatePickingListAsync` (`backend/src/Anela.Heblo.Application/Features/Logistics/Infrastructure/LogisticsExpeditionPickingAdapter.cs`, lines 16–22) maps only `ExportedFiles` and `TotalCount` to `ExpeditionPickingResult`; `OrderIds` is silently discarded. `ExpeditionPickingResult` has no corresponding field.

The only place `OrderIds` appears outside its definition is in the test arrange step (`backend/test/Anela.Heblo.Tests/Features/Logistics/Infrastructure/LogisticsExpeditionPickingAdapterTests.cs`, line 60), where it is set but not asserted — confirming it is not mapped through.

## Why it matters
YAGNI: the field signals to future developers that order IDs are available in this result object, when in practice they are always the empty list initialized at construction. Any code written to read `OrderIds` will silently see an empty collection, producing a silent logic error that is hard to diagnose.

## Suggested fix
Remove `OrderIds` from `PrintPickingListResult`. Update `LogisticsExpeditionPickingAdapterTests.CreatePickingListAsync_TranslatesResultFields` to remove the unused `OrderIds = new List<int> { 1, 2, 3 }` from the arrange step (it adds setup complexity without exercising any real behaviour).

---
_Filed by daily arch-review routine on 2026-06-07._