# Review — GetWarehouseStatisticsHandler: TimeProvider + capacity constant

## Diff reviewed

- `backend/src/Anela.Heblo.Application/Features/Catalog/CatalogConstants.cs` — added `WarehouseCapacityKg = 3000.0`.
- `backend/src/Anela.Heblo.Application/Features/Catalog/UseCases/GetWarehouseStatistics/GetWarehouseStatisticsHandler.cs` — injects `TimeProvider`, uses `_timeProvider.GetUtcNow().UtcDateTime` for `LastUpdated`, references `CatalogConstants.WarehouseCapacityKg` instead of a local `const`.
- `backend/test/Anela.Heblo.Tests/Features/Catalog/GetWarehouseStatisticsHandlerTests.cs` — new file, 3 tests.

## Verification performed

1. **Matches design exactly.** Diffed the committed handler/constants against `design-01.md`'s proposed code — identical, including the `.UtcDateTime` conversion choice and constructor parameter order.
2. **DI registration confirmed.** `TimeProvider` is registered as `services.AddSingleton(TimeProvider.System)` in `backend/src/Anela.Heblo.API/Extensions/ServiceCollectionExtensions.cs:130`. The new constructor parameter resolves at runtime with no additional registration needed (also used by ~15 other handlers already).
3. **Test pattern matches sibling convention.** `GetCatalogDetailHandlerTests.cs` uses the identical `Mock<TimeProvider>()` + `.Setup(tp => tp.GetUtcNow()).Returns(...)` pattern the new test file follows (both `GetUtcNow` is virtual on the abstract `TimeProvider` class, so it mocks cleanly).
4. **Test assertions manually traced against handler logic** — all three tests check out:
   - `Handle_Should_Use_TimeProvider_For_LastUpdated`: mocks `GetUtcNow()` to a fixed instant, asserts `LastUpdated` equals it exactly — proves the race-condition problem from the finding is resolved.
   - `Handle_Should_Return_WarehouseCapacityKg_From_CatalogConstants`: asserts response value equals `CatalogConstants.WarehouseCapacityKg`.
   - `Handle_Should_Calculate_Utilization_Percentage_From_Weighted_Items`: fixture has PROD1 (Product, 500g × 10 eshop = 5kg), PROD2 (Goods, 1000g × 4 eshop = 4kg), PROD3 (Product, no weight, excluded from weight sum but counted in quantity/count), MAT1 (Material, excluded entirely by the `Type == Product || Type == Goods` filter). Expected total weight 9.0kg, quantity 114, product count 3 — all match the handler's actual filter/sum logic line by line.
5. **`dotnet build` on `Anela.Heblo.Tests.csproj`: 0 errors**, 238 pre-existing nullable warnings unrelated to this change (none in the touched files). Confirms the new test file and handler changes compile cleanly against the real `ICatalogRepository`, `CatalogAggregate`, `StockData` types.
6. Attempted to run `dotnet test --filter FullyQualifiedName~GetWarehouseStatisticsHandlerTests` to get an executed pass/fail signal, but the process was killed/lost under heavy concurrent machine load (multiple simultaneous builds from other worktrees) and produced no output before the session ended. Did not get an actual green-checkmark test run. Given the build compiles cleanly and the test logic was manually verified line-by-line against the handler's real implementation (not just against the design doc), I'm confident in the result, but this is a gap versus full verification.

## Findings

None. The implementation is a faithful, minimal, surgical fix of exactly the two issues in the finding:
- `DateTime.UtcNow` → injected `TimeProvider`, consistent with `GetCatalogDetailHandler` / `GetProductMarginsHandler`.
- Hardcoded `3000` → `CatalogConstants.WarehouseCapacityKg`, discoverable alongside the module's other constants.

No unrelated changes. The pre-existing `GetWarehouseStatisticsResponse.WarehouseCapacityKg` property's dead `= 8500` initializer default (always overwritten in `Handle()`) is out of scope and correctly left untouched — noted in `design-01.md` as intentionally out of bounds.

## Outcome

**done** — implementation matches the approved design exactly, is scoped correctly, compiles with zero errors, and the new test's assertions are correct against the actual handler logic. The only shortfall is not obtaining an actual `dotnet test` pass confirmation due to machine load; recommend a follow-up test run before merge if that matters for this repo's process, but nothing in the code suggests it would fail.
