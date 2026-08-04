# Review: Split `IPackingOrderClient` along module ownership lines

## Verdict: done

## What was checked

Read `plan-01.md`, `design-01.md`, `architecture-01.md`, `development-01.md`, and the actual diff
(`git diff HEAD~1`) file-by-file against the design spec.

## Conformance to design-01.md

Every component matches the spec exactly, no deviations:

1. **New contract** `Packaging/Contracts/IPackingOrderCountSource.cs` — created with the two count
   methods, doc comments moved verbatim. ✓
2. **Narrowed contract** `ShoptetOrders/IPackingOrderClient.cs` — now exposes only
   `GetPackingOrderAsync`; `PackingOrder`/`PackingOrderItem` DTOs untouched. ✓
3. **Adapter** `ShoptetApiPackingOrderClient` — now `: IPackingOrderClient, IPackingOrderCountSource`,
   no method body changes, added the one `using`. ✓
4. **DI registration** — `services.AddTransient<IPackingOrderCountSource, ShoptetApiPackingOrderClient>();`
   added next to the existing `IPackingOrderClient` line. ✓
5. **Consumers** — `GetPackingDashboardHandler` and `PackingStatsTile` swapped constructor
   parameter/field type and `using` to `IPackingOrderCountSource`; call sites unchanged. The three
   fetch-only consumers (`ScanPackingOrderHandler`, `ResetOrderShipmentHandler`,
   `CreateOrderShipmentHandler`) were correctly left untouched — confirmed via grep, they still only
   reference `IPackingOrderClient`. ✓
6. **`ModuleBoundariesTests.cs`** — the two stale `PackagingShoptetOrdersAllowlist` entries
   (`GetPackingDashboardHandler`, `PackingStatsTile`) were trimmed; a new `ShipmentLabels ->
   ShoptetOrders` rule + 3-entry allowlist was added for `CreateOrderShipmentHandler`, mirroring the
   existing `Packaging -> ShoptetOrders` rule shape and justification-comment convention. ✓
7. **Test files** — `GetPackingDashboardHandlerTests.cs` and `PackingStatsTileTests.cs` mock types
   swapped to `IPackingOrderCountSource`; other consumer test files correctly left alone since they
   only exercise `GetPackingOrderAsync`. ✓

Scope matches design-01's explicit in/out-of-scope list precisely — no unrelated files touched.

## Independent verification (this review, not just the dev step's self-report)

- `dotnet build` (full solution, from repo root) — **0 errors**, only pre-existing nullable-reference
  warnings unrelated to this change.
- `dotnet test` filtered to the affected area (`ModuleBoundariesTests`, `PackingStatsTileTests`,
  `GetPackingDashboardHandlerTests`, `ScanPackingOrderHandler*`, `ResetOrderShipmentHandlerTests`,
  `CreateOrderShipmentHandlerTests`, `GetPackingOrderHandlerTests`) — **Passed! Failed: 0, Passed: 90,
  Skipped: 0, Total: 90**. Confirms the new `ShipmentLabels -> ShoptetOrders` module boundary rule
  passes with the predicted allowlist (no extra compiler-generated-type entries needed), and that all
  fetch-only consumers are unaffected.
- Grepped all remaining `IPackingOrderClient` references across `backend/src` and `backend/test` —
  matches exactly the design's "out of scope, untouched" list (`ScanPackingOrderHandler`,
  `ResetOrderShipmentHandler`, `CreateOrderShipmentHandler`, `GetPackingOrderHandler`, and their
  respective test files).

The pre-existing `MSB3073`/`AccessMatrixGen` warning during build (noted in development-01.md) was
reproduced independently and is confirmed unrelated to this change — no access-matrix or auth code
was touched, and the build still completes with 0 errors.

## Assessment

The original finding is resolved: `Packaging` no longer has a compile-time dependency on
`ShoptetOrders`'s internal namespace for the count queries — it now owns `IPackingOrderCountSource`
in its own `Contracts/` folder, consistent with the established `IPacking<Noun>Source` convention.
The shared `GetPackingOrderAsync` fetch coupling was deliberately left as-is per a well-reasoned,
previously-approved design decision (Option A), and the previously-untracked `ShipmentLabels ->
ShoptetOrders` coupling is now governed by an architecture test. No functional requirement is unmet,
no architecture conflict, no missing required test, no correctness bug found.
