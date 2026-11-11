# [arch-review] PackingMaterials: GetDailyConsumptionBreakdownHandler fetches full entity graph to read material names (#4486)

`GetDailyConsumptionBreakdownHandler.Handle` calls `GetAllWithAllocationsAsync` only to build `materialNameById`, loading all PackingMaterial entities with Allocations. `IPackingMaterialRepository.GetMaterialNamesByIdsAsync` already returns `IReadOnlyDictionary<int,string>` (used in GetConsumptionHistoryHandler).

Fix: in Handle, compute distinct consumed material ids, call `GetMaterialNamesByIdsAsync` once, and pass `materialNameById` into BuildGroupByMaterial/Product/Order, removing the three duplicate dictionary builds.
