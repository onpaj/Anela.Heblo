# Implementation: add-packingstatstile-unit-tests

## What was implemented
A new xUnit test class, `PackingStatsTileTests`, pinning the three response shapes of `PackingStatsTile.LoadDataAsync`: the happy path (success with counts and packer breakdown, including the null-packer-name → "Neznámý" fallback), the isolated Shoptet failure (inner catch — status stays "success", only the Shoptet-derived fields go null, packer data untouched), and the repository failure (outer catch — status "error", `data` property absent). No production code was changed.

## Files created/modified
- `backend/test/Anela.Heblo.Tests/Features/Packaging/DashboardTiles/PackingStatsTileTests.cs` — 3 `[Fact]` test methods covering FR-1 through FR-4 from the spec, using `Mock<IPackageRepository>`, `Mock<IPackingOrderClient>`, a hand-rolled `FakeTimeProvider : TimeProvider`, and JSON-document assertions against the anonymous response shape.

## Tests
- `LoadDataAsync_AllDependenciesSucceed_ReturnsSuccessWithCountsAndPackers` — happy path + null-packer-name fallback (FR-1, FR-3).
- `LoadDataAsync_ShoptetClientThrows_ReturnsSuccessWithNullCountsAndPackersPopulated` — Shoptet failure isolation (FR-2).
- `LoadDataAsync_RepositoryThrows_ReturnsError` — repository total failure (FR-4).

## How to verify
```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~PackingStatsTileTests"
```
Result: `Passed! - Failed: 0, Passed: 3, Skipped: 0`.

`dotnet format backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --verify-no-changes` — clean, no diffs.

## Notes
Followed the task plan exactly as written (code was already fully specified and grounded in the actual source by the planner/architect phases). No deviations.

## PR Summary
Adds unit test coverage for `PackingStatsTile.LoadDataAsync`, which previously had 0% line coverage. The tile has two independent failure paths — a graceful-degradation inner catch around the Shoptet order-count calls, and a total-failure outer catch around the repository call — that produce different response shapes and were never exercised. The new tests pin both paths plus the happy path, protecting the dashboard's API contract against future refactors that could silently change `ordersBeingPackedCount` from `null` to an error response.

### Changes
- `backend/test/Anela.Heblo.Tests/Features/Packaging/DashboardTiles/PackingStatsTileTests.cs` — new test file, 3 facts, no production code changes.

## Status
DONE
