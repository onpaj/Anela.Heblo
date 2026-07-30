# Plan: Split `IPackingOrderClient` along module ownership lines

## Summary
`IPackingOrderClient` (defined in `ShoptetOrders`) bundles two unrelated concerns — fetching a single packing order, and reading two dashboard counters — and is consumed directly by handlers in `Packaging` (and, as discovered during planning, one handler in `ShipmentLabels` too). This plan narrows the interface along consumer lines: the two Packaging-only counters move to a Packaging-owned contract; the packing-order fetch, which turns out to be genuinely shared across three modules, is deliberately **not** force-split into one interface per consumer, for reasons explained below.

## Context — corrections to the raw finding
The finding's factual basis was verified against the current code and is **mostly correct but incomplete**:

- Confirmed: `IPackingOrderClient` lives in `Anela.Heblo.Application.Features.ShoptetOrders` (`IPackingOrderClient.cs`) and is injected in all four cited `Packaging` files.
- **Correction**: the finding states `GetPackingOrderAsync` is "called only by ShoptetOrders' own `GetPackingOrderHandler`". This is false. `GetPackingOrderAsync` is also called directly by:
  - `Packaging/UseCases/ScanPackingOrder/ScanPackingOrderHandler.cs`
  - `Packaging/UseCases/ResetOrderShipment/ResetOrderShipmentHandler.cs`
  - `ShipmentLabels/UseCases/CreateOrderShipment/CreateOrderShipmentHandler.cs` (a module not mentioned in the finding at all)

  Only `GetOrdersBeingPackedCountAsync` and `GetOrdersBeingProcessedCountAsync` are consumed exclusively by `Packaging` (`GetPackingDashboardHandler`, `PackingStatsTile`), as the finding claimed.
- **Prior-art discovery**: `backend/test/Anela.Heblo.Tests/Architecture/ModuleBoundariesTests.cs` already has a `Packaging -> ShoptetOrders` boundary rule with an explicit, comment-justified allowlist stating the `Packaging` module **"legitimately consumes the `IPackingOrderClient` / `IEshopOrderClient` contracts... This rule pins the 2026-06-05 decoupling in place."** This means a prior review already looked at this exact coupling and chose to accept/pin it rather than fully invert it — the current arch-review finding re-opens a decision that was made deliberately, not by oversight.
- **Gap discovered**: there is no `ShipmentLabels -> ShoptetOrders` boundary rule in `ModuleBoundariesTests.cs` at all (only the reverse direction, `ShoptetOrders -> ShipmentLabels`, is tracked and is clean). `CreateOrderShipmentHandler`'s use of `IPackingOrderClient` is therefore an **untracked** cross-module reference today.

These corrections change the shape of the fix: this is not a clean 1-consumer/1-provider split. `GetPackingOrderAsync` has three consumers, one of which lives in a module the finding never analyzed.

## Functional requirements

**FR-1 — Extract a Packaging-owned counter contract**
- Define `IOrderCountSource` (name TBD in design step) in `Packaging/Contracts/IOrderCountSource.cs`, exposing exactly `GetOrdersBeingPackedCountAsync` and `GetOrdersBeingProcessedCountAsync`.
- `ShoptetApiPackingOrderClient` implements `IOrderCountSource` in addition to its existing interface(s).
- DI: `ShoptetApiAdapterServiceCollectionExtensions` registers `services.AddTransient<IOrderCountSource, ShoptetApiPackingOrderClient>();` alongside the existing registration.
- `GetPackingDashboardHandler` and `PackingStatsTile` are updated to inject `IOrderCountSource` instead of `IPackingOrderClient`, and drop their `using Anela.Heblo.Application.Features.ShoptetOrders;` import.
- **Acceptance criteria**: `dotnet build` succeeds; existing tests `GetPackingDashboardHandlerTests` and `PackingStatsTileTests` pass unmodified except for constructor/mock type changes from `IPackingOrderClient` to `IOrderCountSource`; the `Packaging -> ShoptetOrders` `ModuleBoundariesTests` allowlist entries for `GetPackingDashboardHandler` and `PackingStatsTile` are removed (no longer needed — the reference is gone).

**FR-2 — Decide the disposition of the shared `GetPackingOrderAsync` fetch (design-step decision)**
Because `GetPackingOrderAsync` has three real consumers across three modules (`ShoptetOrders` itself, `Packaging` ×2, `ShipmentLabels` ×1), the strict "consumer owns the contract" pattern would require **three** near-identical single-method interfaces (one per consuming module) all implemented by the same adapter — high ceremony for zero behavior change. Two options, to be resolved in the design step:
- **Option A (minimal, recommended default)**: Leave `GetPackingOrderAsync` on the existing `ShoptetOrders`-owned interface (rename optional, e.g. `IPackingOrderFetcher`, if the design step wants the two remaining methods on `IPackingOrderClient` disambiguated from `IOrderCountSource`). Add the missing `ShipmentLabels -> ShoptetOrders` `ModuleBoundaryRule` to `ModuleBoundariesTests.cs` (with a justifying comment, mirroring the existing `Packaging -> ShoptetOrders` rule) so the `CreateOrderShipmentHandler` reference is tracked and pinned instead of silently ungoverned. This keeps the change surgical and consistent with the prior deliberate decision to accept this coupling.
- **Option B (strict pattern)**: Introduce `Packaging`-owned and `ShipmentLabels`-owned single-method fetch contracts (e.g. `IPackingOrderFetcher` in each module's `Contracts/`), each implemented by `ShoptetApiPackingOrderClient`, with `GetPackingOrderHandler` continuing to use the ShoptetOrders-internal interface directly. Fully removes `Packaging`'s and `ShipmentLabels`' compile-time namespace dependency on `ShoptetOrders`, at the cost of interface duplication.
- **Acceptance criteria (whichever option)**: `ModuleBoundariesTests` reflects the actual, current set of cross-module references (no untracked violations, no stale allowlist entries for references that no longer exist).

**FR-3 — No behavior change**
- `ShoptetApiPackingOrderClient`'s method bodies are untouched; this is a pure interface/DI reshuffle.
- **Acceptance criteria**: no change to any HTTP call, caching, or business logic inside the adapter. All pre-existing tests pass without assertion changes (only mock/constructor type updates).

## Non-functional requirements
- **Maintainability**: reduces blast radius if `ShoptetOrders` or `Packaging` module boundaries are ever hardened or physically split into separate assemblies.
- **No performance impact**: purely a compile-time interface reorganization; no new I/O, no new allocations beyond one extra interface reference per adapter instance.
- **Test coverage preserved**: every test file currently touching `IPackingOrderClient` (`GetPackingDashboardHandlerTests`, `PackingStatsTileTests`, `ScanPackingOrderHandlerPackagePersistenceTests`, `ScanPackingOrderPackerTests`, `ScanPackingOrderHandlerTests`, `ResetOrderShipmentHandlerTests`, `CreateOrderShipmentHandlerTests`, `GetPackingOrderHandlerTests`) must still compile and pass.

## Data model
No data/domain model changes. `PackingOrder` and `PackingOrderItem` DTOs are unaffected and continue to live in `ShoptetOrders` (per FR-2 Option A) or move only if Option B duplicates them per-module (not recommended — DTOs should stay put; only the interface is split).

## Interfaces (affected types)
- `Anela.Heblo.Application.Features.ShoptetOrders.IPackingOrderClient` — shrinks to `GetPackingOrderAsync` only (Option A) or is retired in favor of per-module fetch contracts (Option B).
- New: `Anela.Heblo.Application.Features.Packaging.Contracts.IOrderCountSource` — `GetOrdersBeingPackedCountAsync`, `GetOrdersBeingProcessedCountAsync`.
- `Anela.Heblo.Adapters.ShoptetApi.Orders.ShoptetApiPackingOrderClient` — gains an additional interface (`IOrderCountSource`, and per Option B possibly module-specific fetch interfaces).
- `ShoptetApiAdapterServiceCollectionExtensions` — one new DI registration line (FR-1); possibly more under Option B.
- `ModuleBoundariesTests.cs` — allowlist entries updated/removed for `Packaging -> ShoptetOrders`; new rule added for `ShipmentLabels -> ShoptetOrders` if Option A is chosen.

## Dependencies and scope
- Depends on: `Anela.Heblo.Adapters.ShoptetApi` project (adapter implementation), `Anela.Heblo.Application` (interfaces + handlers), test project (`Anela.Heblo.Tests`).
- **In scope**: interface split, DI registration updates, consumer injection updates, architecture-test allowlist/rule updates, unit test mock-type updates.
- **Out of scope**: any change to adapter method bodies/behavior; any change to `IEshopOrderClient` (a separate, already-correctly-scoped contract used alongside `IPackingOrderClient` in some of the same handlers); renaming `PackingOrder`/`PackingOrderItem` DTOs; addressing the `CompletePackingOrderHandler -> IEshopOrderClient` allowlist entry (unrelated interface).

## Rough plan
1. **Design step**: settle FR-2's Option A vs. B, and confirm final interface name(s) (`IOrderCountSource` vs. alternative; whether `IPackingOrderClient` is renamed or kept as-is for the fetch-only remainder).
2. **Architecture step**: confirm placement (`Packaging/Contracts/`, optionally `ShipmentLabels/Contracts/`), confirm the `ModuleBoundariesTests.cs` rule additions/removals, and confirm DI registration ordering doesn't conflict with existing `IPickingListSource`/`IPackingOrderClient` registrations in the same extension method.
3. **Development step**:
   a. Add `IOrderCountSource` in `Packaging/Contracts/`.
   b. Have `ShoptetApiPackingOrderClient` implement it (and any Option-B fetch interfaces).
   c. Register new DI binding(s) in `ShoptetApiAdapterServiceCollectionExtensions`.
   d. Update `GetPackingDashboardHandler` and `PackingStatsTile` to inject `IOrderCountSource`; drop the now-unneeded `using ShoptetOrders;`.
   e. (If Option A) Add the `ShipmentLabels -> ShoptetOrders` rule to `ModuleBoundariesTests.cs` with a justifying comment; trim the now-obsolete `GetPackingDashboardHandler`/`PackingStatsTile` entries out of `PackagingShoptetOrdersAllowlist`.
   f. (If Option B) Add `Packaging`- and `ShipmentLabels`-owned fetch contracts, rewire `ScanPackingOrderHandler`, `ResetOrderShipmentHandler`, `CreateOrderShipmentHandler`; remove the corresponding allowlist entries entirely (no longer needed since the boundary is closed).
   g. Update affected unit tests' mock setups to the new interface type(s).
   h. Run `dotnet build`, `dotnet format`, and the full backend test suite.

## Open questions
1. **FR-2 decision (Option A vs. B)** — is closing the `Packaging`/`ShipmentLabels` → `ShoptetOrders` compile-time dependency for the *fetch* operation worth three near-duplicate interfaces, or is pinning/tracking the existing accepted coupling (as already done for `Packaging`) sufficient? Recommend **Option A** by default per the "surgical changes" project guideline and because a prior review already accepted this coupling deliberately — but flagging for explicit sign-off since it only partially satisfies the original finding's intent.
2. **Naming** — is `IOrderCountSource` acceptable, or does the design step prefer something more specific (e.g. `IPackingCounterSource`, mirroring the existing `IPackingCarrierCoolingSource`/`IPackingProductSource` naming convention in `ShoptetOrders/Contracts/`)?
3. Should `IPackingOrderClient` be renamed at all if Option A is chosen and it still only carries `GetPackingOrderAsync`? A rename (e.g. to `IPackingOrderFetcher`) more accurately reflects its narrowed scope but touches more files (all three consumers) for a cosmetic gain; keeping the name unchanged is lower-risk. Recommend keeping the existing name unless the design step disagrees.
