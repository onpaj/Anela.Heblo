# Code Review: replace-applyenrichment-call-with-inline-cooling-loop

## Summary

The implementation replaces the `ApplyEnrichment(...)` call in `ShoptetApiPackingOrderClient.GetPackingOrderAsync` with an inline `foreach` loop applying only the cooling enrichment, exactly as specified in the task context and task plan. The diff is a single, surgical, 5-line-for-5-line block replacement with no other changes. Build, format check, and all relevant regression tests pass.

## Review Result: PASS

### task: replace-applyenrichment-call-with-inline-cooling-loop
**Status:** PASS

Verification performed:
- Confirmed via `git diff` that the change is exactly the one-block replacement specified: the `ApplyEnrichment(order.Items, new Dictionary<string, decimal>(), new Dictionary<string, string>(), coolingByCode)` call is replaced by a `foreach (var item in order.Items) { if (coolingByCode.TryGetValue(item.ProductCode, out var cooling)) item.Cooling = cooling; }` loop, at the same indentation and same point in the method, with no other line touched.
- Confirmed `ShoptetApiExpeditionListSource` still has exactly one remaining reference in the file (`MapToExpeditionOrder`, line 64), so the `using` import is still justified.
- `dotnet build Anela.Heblo.sln` — succeeded, 0 errors, no new warnings.
- `dotnet format Anela.Heblo.sln --verify-no-changes` — exited 0, no output.
- `ShoptetApiPackingOrderClientTests` — 12/12 passed, including both named cooling-behavior tests (`GetPackingOrderAsync_ComputesCooling_FromCarrierMatrixAndCatalog`, `GetPackingOrderAsync_NotCooled_WhenCarrierMatrixEmpty`).
- `ShoptetApiExpeditionListSourceTests` — 94/94 passed.
- Shoptet adapter project `ShoptetApiExpeditionListSource*` tests — 8/8 passed.
- Full backend suite across all 8 test projects run; the 190 failing tests are all pre-existing environment limitations unrelated to this change (Testcontainers/Docker unavailable for Postgres-backed integration tests; Shoptet/Flexi `*IntegrationTests` requiring live external API access unavailable in this sandbox) — none reference `Packing`, `Cooling`, or the touched file. The 5 unaffected test projects (HomeAssistant, OpenMeteo, OpenAI, Plaud, Logeto) passed 100%.
- FR-2 (preserve `ApplyEnrichment` for the expedition/picking-list path) confirmed: `ApplyEnrichment` itself is untouched, and its other real caller (`PickingListBatchProcessor.WriteEnrichmentAsync`) is unaffected — verified by the passing `ShoptetApiExpeditionListSourceTests`/adapter-project runs above.
- Commit `bdd0e56` matches the task's prescribed commit scope (single file only).

No functional requirement is unmet, no architecture guideline is violated, and no correctness bug was found.

## Docs to Update

(Omit this section entirely if no documentation changes are needed)

None — this is an internal, behavior-preserving refactor with no public API, CLI, config, or operational change.

## Overall Notes

None.
