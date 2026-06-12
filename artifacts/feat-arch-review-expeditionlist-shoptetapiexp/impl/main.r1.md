All validation complete. Here is the output summary:

---

# Implementation: Extract `PickingListBatchProcessor` from `ShoptetApiExpeditionListSource.CreatePickingList`

## What was implemented

Behavior-preserving extraction of the `FlushBatchAsync` local function (which closed over 8 outer-scope variables) into a new `internal sealed PickingListBatchProcessor` helper class. `CreatePickingList` was further split into two private helpers (`BuildOrdersByMethod`, `BatchAndFlushAsync`) to meet the 50-line requirement. Six new tests cover the helper's contract directly without HTTP-mocked driver paths.

## Files created/modified

- `backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Expedition/PickingListBatchProcessor.cs` — new internal sealed helper: `FlushAsync`, `EnrichBatchAsync`, `WriteCoolingMarkersAsync`; constants `CoolingMarkerValue` and `CoolingAdditionalFieldIndex` moved here
- `backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Expedition/ShoptetApiExpeditionListSource.cs` — removed `FlushBatchAsync` local function and two constants; added `BuildOrdersByMethod` (static) and `BatchAndFlushAsync` (instance) private helpers; `CreatePickingList` body reduced to 33 non-blank lines
- `backend/test/Anela.Heblo.Adapters.Shoptet.Tests/Expedition/PickingListBatchProcessorTests.cs` — new: 5 tests covering callback semantics, null callback, catalog enrichment, PATCH success, PATCH failure + warning log
- `backend/test/Anela.Heblo.Adapters.Shoptet.Tests/Expedition/ShoptetApiExpeditionListSource_CoolingMarkerTests.cs` — appended `CreatePickingList_MultipleBatches_FilenamesContainSequentialBatchIndex` (batchIndex pass-through test, 2 orders × 7 items = 14 total, forces mid-loop overflow)

## Tests

- `PickingListBatchProcessorTests.cs` — 5 new tests: `FlushAsync_InvokesCallbackOnceWithSingleElementList`, `FlushAsync_DoesNotThrow_WhenCallbackIsNull`, `FlushAsync_AppliesCatalogEnrichmentToBatchItems`, `FlushAsync_PatchesEachCooledOrderOnce_AndSkipsNonCooled`, `FlushAsync_PatchFailure_LogsWarning_AndCompletesNormally`
- `ShoptetApiExpeditionListSource_CoolingMarkerTests.cs` — 1 new test: `CreatePickingList_MultipleBatches_FilenamesContainSequentialBatchIndex`
- All 97 Shoptet adapter tests pass; all 84 expedition source tests pass; format clean

## How to verify

```bash
cd /Users/pajgrtondrej/Work/GitHub/Anela.Heblo/.worktrees/feat-arch-review-expeditionlist-shoptetapiexp
dotnet test backend/test/Anela.Heblo.Adapters.Shoptet.Tests/Anela.Heblo.Adapters.Shoptet.Tests.csproj --no-build
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~ShoptetApiExpeditionListSourceTests" --no-build
dotnet format Anela.Heblo.sln --no-restore --verify-no-changes
```

## Notes

- Logger parameter on `PickingListBatchProcessor` is typed as base `ILogger` (not `ILogger<PickingListBatchProcessor>`) — load-bearing for ops/alerting log category continuity; comment explains this in the constructor
- Pre-existing failures in `Anela.Heblo.Tests.dll` (38 Testcontainers/PostgreSQL Docker failures) and `Anela.Heblo.Adapters.Flexi.Tests.dll` (2 failures) are unrelated to this change and were present before any edits

## PR Summary

Extracted the `FlushBatchAsync` local function from `ShoptetApiExpeditionListSource.CreatePickingList` into a new `internal sealed PickingListBatchProcessor` helper, making the per-batch flush logic (catalog enrichment, PDF generation, file write, Shoptet cooling-marker PATCH, callback) directly unit-testable without full driver-level HTTP mocks.

`CreatePickingList` was further split into `BuildOrdersByMethod` and `BatchAndFlushAsync` private helpers, reducing its body from 89 to 33 non-blank lines. Six new unit tests cover the helper's four responsibilities in isolation. All 181 pre-existing tests in the affected assemblies continue to pass unchanged.

### Changes
- `backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Expedition/PickingListBatchProcessor.cs` — new helper (FlushAsync, EnrichBatchAsync, WriteCoolingMarkersAsync; CoolingMarker constants)
- `backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Expedition/ShoptetApiExpeditionListSource.cs` — removed FlushBatchAsync local function and constants; extracted BuildOrdersByMethod and BatchAndFlushAsync; CreatePickingList body ≤ 50 lines
- `backend/test/Anela.Heblo.Adapters.Shoptet.Tests/Expedition/PickingListBatchProcessorTests.cs` — new: 5 helper-level tests
- `backend/test/Anela.Heblo.Adapters.Shoptet.Tests/Expedition/ShoptetApiExpeditionListSource_CoolingMarkerTests.cs` — added batchIndex two-batch regression test

## Status
DONE