## Review Result: CLEAN

### Blocking (correctness)
- None

### Advisory (cleanup)
- None

## Summary

The diff is a small, precise interface-ownership-inversion fix. `CompleteDeliveredOrdersJob` now depends on the narrow, `ShoptetOrders`-owned `IShipmentDeliveryChecker` (single method, `Task<bool> HasDeliveredShipmentAsync(string orderCode, CancellationToken ct = default)`) instead of `ShipmentLabels`' full six-method `IShipmentClient`. `ShipmentLabelsShipmentDeliveryCheckerAdapter` (internal, in `ShipmentLabels/Infrastructure/`) implements the new interface by pure delegation to `IShipmentClient.HasDeliveredShipmentAsync`, with a matching signature verified against `IShipmentClient.cs`. DI registration (`AddTransient<IShipmentDeliveryChecker, ShipmentLabelsShipmentDeliveryCheckerAdapter>()`) is added to `ShipmentLabelsModule` — the provider owns the registration, matching the existing `IPackingCarrierCoolingSource`/`IPackingProductSource` convention in this codebase. The job's `ExecuteAsync` control flow, logging, and metadata are untouched; only the field/constructor parameter type changed. A new `ModuleBoundariesTests` rule (`"ShoptetOrders -> ShipmentLabels"`, empty allowlist) pins the fix so a direct `IShipmentClient` reference cannot be reintroduced into `ShoptetOrders`. Test coverage: 2 new adapter delegation tests, 1 new DI-wiring test, and the 9 pre-existing `CompleteDeliveredOrdersJobTests` all pass against the retyped mock with no behavioral changes needed. Full solution build is clean (0 errors), and the full backend test suite shows no new failures — the 76 failing tests are pre-existing Docker/testcontainers integration tests unrelated to this change (no Docker daemon in this sandbox).
