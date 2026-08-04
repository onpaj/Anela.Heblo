# Development: Split `IPackingOrderClient` along module ownership lines

Implements design-01.md (approved without changes in architecture-01.md) exactly as specified —
no deviations.

## Summary

`IPackingOrderClient` (in `ShoptetOrders`) previously bundled three methods serving two different
modules: `GetPackingOrderAsync` (consumed by `ShoptetOrders`, `Packaging`, `ShipmentLabels`) and
`GetOrdersBeingPackedCountAsync`/`GetOrdersBeingProcessedCountAsync` (consumed only by `Packaging`).
The two count methods are split into a new `Packaging`-owned contract, `IPackingOrderCountSource`,
following the consumer-owns-the-contract pattern already used for `IPackingCarrierCoolingSource`/
`IPackingProductSource`. `GetPackingOrderAsync` stays on `IPackingOrderClient` in `ShoptetOrders`
unchanged (Option A from design-01 — the shared fetch coupling was already deliberately pinned by
a prior 2026-06-05 decoupling and splitting it further was rejected as ceremony without payoff).
The previously untracked `ShipmentLabels -> ShoptetOrders` coupling (via `CreateOrderShipmentHandler`)
is now tracked with a new `ModuleBoundaryRule`, mirroring the existing `Packaging -> ShoptetOrders`
rule.

## Files created

- `backend/src/Anela.Heblo.Application/Features/Packaging/Contracts/IPackingOrderCountSource.cs`
  — new `Packaging`-owned contract with `GetOrdersBeingPackedCountAsync`/`GetOrdersBeingProcessedCountAsync`
  (doc comments moved verbatim from the old interface).

## Files changed

- `backend/src/Anela.Heblo.Application/Features/ShoptetOrders/IPackingOrderClient.cs` — removed the
  two count methods; now exposes only `GetPackingOrderAsync`. `PackingOrder`/`PackingOrderItem` DTOs
  untouched.
- `backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Orders/ShoptetApiPackingOrderClient.cs` —
  now implements both `IPackingOrderClient` and `IPackingOrderCountSource`. No method body changes.
- `backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/ShoptetApiAdapterServiceCollectionExtensions.cs`
  — added `services.AddTransient<IPackingOrderCountSource, ShoptetApiPackingOrderClient>();` next to
  the existing `IPackingOrderClient` registration, plus the new `using` for `Packaging.Contracts`.
- `backend/src/Anela.Heblo.Application/Features/Packaging/UseCases/GetPackingDashboard/GetPackingDashboardHandler.cs`
  — constructor/field swapped from `IPackingOrderClient` to `IPackingOrderCountSource`; call sites
  unchanged.
- `backend/src/Anela.Heblo.Application/Features/Packaging/DashboardTiles/PackingStatsTile.cs` — same
  swap as above.
- `backend/test/Anela.Heblo.Tests/Architecture/ModuleBoundariesTests.cs`:
  - Trimmed the two now-stale `PackagingShoptetOrdersAllowlist` entries for
    `GetPackingDashboardHandler -> IPackingOrderClient` and `PackingStatsTile -> IPackingOrderClient`
    (these types no longer reference the `ShoptetOrders` namespace at all).
  - Added `ShipmentLabelsShoptetOrdersAllowlist` (three entries: `IPackingOrderClient`, `PackingOrder`,
    `PackingOrderItem`, matching `CreateOrderShipmentHandler`'s actual usage) and a new
    `ShipmentLabels -> ShoptetOrders` `ModuleBoundaryRule`, mirroring the existing `Packaging ->
    ShoptetOrders` rule shape/justification comment.
- `backend/test/Anela.Heblo.Tests/Features/Packaging/GetPackingDashboardHandlerTests.cs` and
  `backend/test/Anela.Heblo.Tests/Features/Packaging/DashboardTiles/PackingStatsTileTests.cs` —
  mock type swapped from `Mock<IPackingOrderClient>` to `Mock<IPackingOrderCountSource>`; test bodies
  and assertions unchanged.

## Out of scope (confirmed untouched)

`IEshopOrderClient`, `PackingOrder`/`PackingOrderItem` DTO placement, `ScanPackingOrderHandler`,
`ResetOrderShipmentHandler`, `CreateOrderShipmentHandler`, `GetPackingOrderHandler` (all keep
injecting `IPackingOrderClient.GetPackingOrderAsync` unchanged), and the
`CompletePackingOrderHandler -> IEshopOrderClient` allowlist entry.

## Verification

- `dotnet build` (full solution) — 0 errors (pre-existing warnings only, unrelated to this change).
- `dotnet format --verify-no-changes` — clean, no formatting diffs.
- `dotnet test --filter "FullyQualifiedName~ModuleBoundariesTests|FullyQualifiedName~GetPackingDashboardHandlerTests|FullyQualifiedName~PackingStatsTileTests|FullyQualifiedName~ShoptetApiPackingOrderClientTests|FullyQualifiedName~ScanPackingOrderHandlerTests|FullyQualifiedName~ResetOrderShipmentHandlerTests|FullyQualifiedName~CreateOrderShipmentHandlerTests|FullyQualifiedName~GetPackingOrderHandlerTests|FullyQualifiedName~ScanPackingOrderHandlerPackagePersistenceTests|FullyQualifiedName~ScanPackingOrderPackerTests"`
  → **Passed! Failed: 0, Passed: 107, Skipped: 0, Total: 107** (Anela.Heblo.Tests.dll). Confirms the
  new `ShipmentLabels -> ShoptetOrders` module boundary rule (with the predicted three-entry
  allowlist) passes as-is — no additional compiler-generated-type entries were needed. Confirms the
  trimmed `Packaging -> ShoptetOrders` allowlist doesn't break the rule (no stale-entry assertion
  exists, as architecture-01.md noted). Confirms all fetch-only consumers (`ScanPackingOrderHandler`,
  `ResetOrderShipmentHandler`, `CreateOrderShipmentHandler`, `GetPackingOrderHandler`) are unaffected.

To reproduce: from the repo root, `export PATH="$HOME/.dotnet:$PATH"` then run the commands above
(or the full `dotnet test` for the complete suite).

## Note (unrelated, not fixed)

During `dotnet build`, the `Anela.Heblo.API` project's access-matrix-generation post-build step threw
an unhandled `JsonException` ("'/' is an invalid start of a value") in `Anela.Heblo.AccessMatrixGen`
and exited with code 134, logged as MSBuild warning MSB3073. This is a pre-existing environment/tool
issue unrelated to this change (no `IPackingOrderClient`/`IPackingOrderCountSource`/access-matrix
code was touched) — the build still completed successfully (0 errors) and all tests passed. Left
untouched per the surgical-changes rule; flagging for awareness since it's unusual output.
