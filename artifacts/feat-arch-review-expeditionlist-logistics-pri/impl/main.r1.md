All tasks complete. Here is the output artifact:

---

# Implementation: Remove unused `OrderIds` field from `PrintPickingListResult`

## What was implemented

Removed the dead `OrderIds` property from the internal application-layer DTO `PrintPickingListResult` and removed its corresponding unused arrange step from the adapter test. The field was initialized empty, never written by any producer (`ShoptetApiExpeditionListSource.CreatePickingList`), and never read by any consumer (`LogisticsExpeditionPickingAdapter.CreatePickingListAsync`). Exactly two lines deleted across two files; no runtime behaviour change.

## Files created/modified

- `backend/src/Anela.Heblo.Application/Features/Logistics/Picking/PrintPickingListResult.cs` — removed `public IList<int> OrderIds { get; set; } = new List<int>();` (line 7)
- `backend/test/Anela.Heblo.Tests/Features/Logistics/Infrastructure/LogisticsExpeditionPickingAdapterTests.cs` — removed `OrderIds = new List<int> { 1, 2, 3 },` from the arrange block of `CreatePickingListAsync_TranslatesResultFields`

## Tests

- `backend/test/Anela.Heblo.Tests/Features/Logistics/Infrastructure/LogisticsExpeditionPickingAdapterTests.cs` — all 4 existing tests pass; `CreatePickingListAsync_TranslatesResultFields` retains its assertions on `ExportedFiles` and `TotalCount`

## How to verify

```bash
cd backend
dotnet build Anela.Heblo.sln --nologo            # Build succeeded, 0 errors
dotnet test Anela.Heblo.sln --filter "FullyQualifiedName~LogisticsExpeditionPickingAdapterTests" --nologo  # Failed: 0, Passed: 4
grep -rn "OrderIds" src/Anela.Heblo.Application/Features/Logistics/ --include="*.cs"  # no output
git log -1 --stat                                # 2 files changed, 2 deletions(-)
```

## Notes

Baseline confirmed 70 warnings before the change; post-change build also shows 70 warnings (no regression). `dotnet format --verify-no-changes` confirmed no formatting drift. The `OrderIds` symbol still appears in the repo in unrelated places (`ChangePurchaseOrderIdsToInt` EF migration name, React `newOrderIds` callback) — none of these reference `PrintPickingListResult.OrderIds`.

## PR Summary

Removes `PrintPickingListResult.OrderIds`, a dead property that was initialized to an empty list by the constructor default but never populated by `ShoptetApiExpeditionListSource.CreatePickingList` and never consumed by `LogisticsExpeditionPickingAdapter.CreatePickingListAsync`. The field violated YAGNI and created a misleading API surface that could lead a future developer to rely on it and produce a silent logic error. The cross-feature contract `ExpeditionPickingResult` never had `OrderIds` and is untouched; the OpenAPI surface is unaffected.

### Changes
- `backend/src/Anela.Heblo.Application/Features/Logistics/Picking/PrintPickingListResult.cs` — deleted the `OrderIds` property declaration
- `backend/test/Anela.Heblo.Tests/Features/Logistics/Infrastructure/LogisticsExpeditionPickingAdapterTests.cs` — deleted the dead `OrderIds` initializer from the `CreatePickingListAsync_TranslatesResultFields` arrange block

## Status

DONE