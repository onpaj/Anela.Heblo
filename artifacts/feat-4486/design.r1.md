# Design
Handler only (`GetDailyConsumptionBreakdownHandler.cs`):
1. After the empty check: `var ids = consumptions.Select(c => c.PackingMaterialId).Distinct().ToList(); var materialNameById = await _repository.GetMaterialNamesByIdsAsync(ids, cancellationToken);`
2. Change `BuildGroupByMaterial/Product/Order` signatures to `(List<PackingMaterialConsumption> consumptions, IReadOnlyDictionary<int,string> materialNameById)`; delete the local `ToDictionary` lines.
3. Remove unused `using` only if it becomes unused (Domain using still needed for PackingMaterialConsumption).
No UI, API or DB changes.
