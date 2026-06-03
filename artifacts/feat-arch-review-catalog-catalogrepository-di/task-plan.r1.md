Plan saved to `docs/superpowers/plans/2026-06-01-decouple-catalog-repository-from-providers.md`.

The plan is decomposed into 12 tasks executing TDD-style:
- **Task 1**: Add three Catalog-owned source contracts (`ICatalogTransportSource`, `ICatalogPurchaseSource`, `ICatalogManufactureSource`).
- **Tasks 2–7**: Failing-test → implementation pairs for each of the three provider adapters (Logistics with golden aggregation test, Purchase, Manufacture).
- **Task 8**: Provider-owned DI registrations in `LogisticsModule` / `PurchaseModule` / `ManufactureModule`.
- **Task 9**: Rewire `CatalogRepository` — drop six provider deps (incl. dead `IManufactureClient`), add three contracts, delete five private helpers, retain the `ManufactureHistoryRecord` `using` with a justification comment.
- **Task 10**: Update the two existing `CatalogRepository*Tests` files to the new constructor shape, add focused `Refresh*` regression tests, add a DI smoke test for the three contracts.
- **Task 11**: Add the three boundary rules to `ModuleBoundariesTests` with allowlists covering the pre-existing handler `IManufactureClient` carry-overs and the deliberate `ManufactureHistoryRecord` leak.
- **Task 12**: Full validation pass (build / format / test / diff scope check).

Per the pipeline note, skipping the execution-handoff prompt.