## Review Result: CLEAN

### Blocking (correctness)
- None

### Advisory (cleanup)
- None

## Notes

Reviewed the real feature diff (`git diff $(git merge-base origin/main HEAD)...HEAD`,
scoped to `backend/src` and `backend/test` — the `artifacts/feat-4166/**` pipeline
markdown was excluded from review as non-code) against `spec.r1.md`.

Code changes (5 files):
- `backend/src/.../PackingMaterials/Mapping/PackingMaterialMapper.cs` (new) — `internal static class` with `ToDto(PackingMaterial material, decimal? forecastedDays)`, mapping all 9 `PackingMaterialDto` fields including `ConsumptionTypeText` via the existing `PackingMaterialsTextHelper.ConsumptionTypeText`. Matches FR-1 and the `JournalEntryMapper` precedent exactly.
- `CreatePackingMaterialHandler.cs`, `UpdatePackingMaterialHandler.cs` — inline `new PackingMaterialDto { ... }` replaced with `PackingMaterialMapper.ToDto(material, forecastedDays: null)`, preserving the "no forecast for Create/Update" behavior explicitly (FR-2).
- `UpdatePackingMaterialQuantityHandler.cs`, `GetPackingMaterialsListHandler.cs` — replaced with `PackingMaterialMapper.ToDto(material, displayForecast)`; the `decimal.MaxValue`→`null` guard and `Math.Round(..., 1)` forecast computation are untouched and still run upstream of the mapper call, as required by FR-2 and the spec's Out-of-Scope section. `GetPackingMaterialsListHandler`'s `withForecast`/`withoutForecast`/`totalLogs` counters (feeding its `LogDebug` call) are computed from `displayForecast`/`recentLogs` before the mapper call, so extraction doesn't touch them — verified by reading the surrounding code, not just the diff.
- `PackingMaterialMapperTests.cs` (new) — covers full field mapping, `forecastedDays: null` passthrough, and all three `ConsumptionType` → text cases. Verified against `PackingMaterialsTextHelper.ConsumptionTypeText`'s actual switch (`PerOrder`→"za zakázku", `PerProduct`→"za produkt", `PerDay`→"za den") — the test's expected strings are correct.

FR-3 (no behavior change): confirmed by inspection — every field assignment in the four call sites is byte-identical to what it replaced, just relocated into the shared mapper.

Out-of-scope items (`DeletePackingMaterialHandler`, `GetPackingMaterialLogsHandler`, AutoMapper, forecast-computation logic, frontend) are untouched, matching the spec.

No correctness bugs found. No cleanups worth flagging — the diff is minimal and surgical, touching only what FR-1/FR-2 require.

> Process note: no `Task`/subagent-spawning tool was available in this session, so this
> code-review round was performed directly by the orchestrator process following
> `.agents/code-reviewer.md`'s review philosophy, decision rule, and output format in full,
> rather than dispatched to an isolated subagent call.
