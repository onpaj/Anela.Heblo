# Implementation: add-purchase-orders-in-transit-tile-format-tests (r1)

## Status
DONE

## Summary
Created `backend/test/Anela.Heblo.Tests/Features/Purchase/DashboardTiles/PurchaseOrdersInTransitTileTests.cs` exactly as specified in the task context, driving the private `FormatAmountInThousands` method through the tile's public `LoadDataAsync()` entry point with a mocked `IPurchaseOrderRepository`. No production code was created or modified.

### Changes
- `backend/test/Anela.Heblo.Tests/Features/Purchase/DashboardTiles/PurchaseOrdersInTransitTileTests.cs` (new) — 1 `[Fact]` covering the zero-amount branch (`"0"`, not `"0k"`) and 1 `[Theory]` with 8 `[InlineData]` cases covering the integer/decimal boundary (999, 1000, 1001), the integer-thousands branch (1000, 5000, 10000), and the decimal-thousands branch (1500, 9999, 999999).

## Verification performed
- `dotnet test backend/test/Anela.Heblo.Tests/ --filter "FullyQualifiedName~PurchaseOrdersInTransitTileTests"` -> Passed: 9, Failed: 0.
- `dotnet test backend/test/Anela.Heblo.Tests/` (full suite) -> Passed: 7120, Failed: 110, all 110 pre-existing and environmental: every sampled failure is `Anela.Heblo.Tests.Features.Leaflet.Integration.LeafletRepositoryIntegrationTests` failing with `Docker is either not running or misconfigured` (Testcontainers-based integration tests; this sandbox has no Docker daemon). No failure relates to Purchase or the new test file. No regression introduced.
- `dotnet format Anela.Heblo.sln --no-restore --include backend/test/Anela.Heblo.Tests/Features/Purchase/DashboardTiles/PurchaseOrdersInTransitTileTests.cs` -> no diffs.
- `dotnet build backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --no-restore` -> Build succeeded, 0 errors (pre-existing nullable warnings only, unrelated to this file).

## Notes
- `[InlineData]` uses `int amount` (not `decimal`) because xUnit's `InlineDataAttribute` cannot carry `decimal` constants; `BuildOrderWithAmount(decimal amount)` receives it via the existing implicit `int -> decimal` conversion.
- All test-case values reproduced the exact expected strings given in the task context/spec on the first run — no discrepancy against `FormatAmountInThousands`'s existing behavior was found, so no open question to flag.
