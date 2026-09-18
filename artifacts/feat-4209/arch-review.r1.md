# Architecture Review: Decouple Packaging and ExpeditionList from ShoptetOrders.IEshopOrderClient

## Skip Design: true

Backend-only refactor: new C# interfaces, two adapter classes, DI registration changes, and handler
constructor updates. No UI, screen, or visual component changes.

## Architectural Fit Assessment

This fits an already-established and documented pattern in this codebase (`docs/architecture/development_guidelines.md`,
"When module A needs read-only access to data in module B, the dependency must invert: the consumer
owns the contract, the provider implements an adapter"). Verified working instances:

- `Leaflet.Contracts.ILeafletKnowledgeSource` ← implemented by `KnowledgeBase.Infrastructure.KnowledgeBaseLeafletSourceAdapter`,
  registered in `KnowledgeBaseModule.AddKnowledgeBaseModule`.
- `ShoptetOrders.Contracts.IShipmentDeliveryChecker` ← implemented by `ShipmentLabels.Infrastructure.ShipmentLabelsShipmentDeliveryCheckerAdapter`,
  registered in `ShipmentLabelsModule.AddShipmentLabelsModule`. (Note this is ShoptetOrders *consuming* a
  contract it owns, provided by ShipmentLabels — the mirror image of the coupling this issue fixes, and proof
  the pattern already flows in both directions around `ShoptetOrders` in this codebase.)

The spec's proposed `IPackedOrderStatusUpdater` / `IOrderStatusReader` design is architecturally correct and
requires no deviation from the established pattern. No new abstractions, frameworks, or cross-cutting
infrastructure are needed.

**Important pre-existing constraint the spec did not surface:** `backend/test/Anela.Heblo.Tests/Architecture/ModuleBoundariesTests.cs`
already has a `"Packaging -> ShoptetOrders"` rule (`InspectedNamespacePrefix: Features.Packaging`,
forbidding `Features.ShoptetOrders`) with an explicit allowlist (`PackagingShoptetOrdersAllowlist`) that
**currently legitimizes** `ScanPackingOrderHandler -> IEshopOrderClient` and
`CompletePackingOrderHandler -> IEshopOrderClient` as "the 2026-06-05 decoupling", i.e. issue #3721's actual
resolution pinned this exact coupling in place as accepted rather than removing it. This issue reopens that
decision for `IEshopOrderClient` specifically. `ExpeditionList -> ShoptetOrders` has **no rule at all** today —
the coupling in `PrintExpeditionOrderHandler` is currently unenforced and undetected by this test suite. Both
facts change the implementation guidance below (see Prerequisites and Interfaces sections).

## Proposed Architecture

### Component Overview

```
Packaging (consumer)                     ShoptetOrders (provider, unchanged internally)
├── Contracts/
│   └── IPackedOrderStatusUpdater.cs  ←───implements───┐
├── UseCases/ScanPackingOrder/                          │
│   └── ScanPackingOrderHandler.cs  ──injects──┐         │
└── UseCases/CompletePackingOrder/              │        │
    └── CompletePackingOrderHandler.cs ──injects┤        │
                                                 ▼        │
                                    IPackedOrderStatusUpdater
                                                 ▲        │
                                                 │   Infrastructure/
                                                 └───ShoptetOrdersPackedOrderStatusUpdaterAdapter.cs
                                                          │ delegates to
                                                          ▼
                                                    IEshopOrderClient.MarkAsPackedAsync
                                                      (unchanged, internal to ShoptetOrders)

ExpeditionList (consumer)
├── Contracts/
│   └── IOrderStatusReader.cs  ←───implements───┐
└── UseCases/PrintExpeditionOrder/               │
    └── PrintExpeditionOrderHandler.cs ──injects─┤
                                                  ▼
                                          IOrderStatusReader
                                                  ▲
                                                  │   Infrastructure/
                                                  └───ShoptetOrdersOrderStatusReaderAdapter.cs
                                                          │ delegates to
                                                          ▼
                                                IEshopOrderClient.GetOrderStatusIdAsync
```

Both adapters are registered by the provider (`ShoptetOrdersModule.AddShoptetOrdersModule`), never by the
consumer modules — this is the one DI wiring change outside the three handler files.

### Key Design Decisions

#### Decision 1: Adapter naming and location
**Options considered:** Name adapters after the contract only (e.g. `PackedOrderStatusUpdaterAdapter`) vs.
prefixing with the provider module name (e.g. `ShoptetOrdersPackedOrderStatusUpdaterAdapter`), matching
`ShipmentLabelsShipmentDeliveryCheckerAdapter` and `KnowledgeBaseLeafletSourceAdapter`.
**Chosen approach:** `{Provider}{ContractName}Adapter`, placed in
`Features/ShoptetOrders/Infrastructure/` (the folder already exists — it currently holds
`Infrastructure/Jobs/CompleteDeliveredOrdersJob.cs`).
**Rationale:** Matches the two existing adapter naming conventions in this codebase exactly; avoids
inventing a third convention for a pattern that already has two consistent examples.

#### Decision 2: One interface per consumer, not one shared interface
**Options considered:** A single `IShoptetOrderStatusClient` used by both Packaging and ExpeditionList vs.
two separate single-method interfaces owned by each consumer.
**Chosen approach:** Two separate interfaces, exactly as the spec proposes (`IPackedOrderStatusUpdater` in
Packaging, `IOrderStatusReader` in ExpeditionList).
**Rationale:** The consumer-owns-contract pattern is per-consumer by design (see `ILeafletKnowledgeSource`,
`ISmartsuppKnowledgeSource`, `IArticleKnowledgeSource` — three separate KnowledgeBase-facing contracts, not
one shared one, even though all three are read-only KB queries). A shared interface would recreate exactly
the coupling problem this issue fixes: two unrelated modules depending on one contract neither fully owns,
and a change driven by one consumer's needs risking the other's compile/test surface.

#### Decision 3: Adapters delegate to `IEshopOrderClient`, not to `Anela.Heblo.Adapters.ShoptetApi` directly
**Options considered:** Have the new adapters call `Anela.Heblo.Adapters.ShoptetApi.Orders.ShoptetOrderClient`
(the concrete Shoptet REST implementation) directly, bypassing `IEshopOrderClient`.
**Chosen approach:** Adapters depend on and delegate to `IEshopOrderClient` (unchanged), which is already
correctly resolved via DI from `ShoptetApiAdapterServiceCollectionExtensions`.
**Rationale:** `IEshopOrderClient` already IS the correct seam between the Application layer and the Shoptet
adapter; this issue is only about who inside the Application layer is allowed to see that seam, not about
replacing it. Bypassing it would introduce a second, redundant Application→Adapter boundary and contradicts
NFR-3 ("no change to `IEshopOrderClient` itself... or its implementation").

## Implementation Guidance

### Directory / Module Structure

New files:
- `backend/src/Anela.Heblo.Application/Features/Packaging/Contracts/IPackedOrderStatusUpdater.cs`
- `backend/src/Anela.Heblo.Application/Features/ShoptetOrders/Infrastructure/ShoptetOrdersPackedOrderStatusUpdaterAdapter.cs`
- `backend/src/Anela.Heblo.Application/Features/ExpeditionList/Contracts/IOrderStatusReader.cs`
- `backend/src/Anela.Heblo.Application/Features/ShoptetOrders/Infrastructure/ShoptetOrdersOrderStatusReaderAdapter.cs`

Modified files:
- `ScanPackingOrderHandler.cs` — replace `IEshopOrderClient _eshopOrderClient` field/ctor param with
  `IPackedOrderStatusUpdater`; update the one call site (`TryMarkAsPackedAsync`).
- `CompletePackingOrderHandler.cs` — same replacement; update the one call site.
- `PrintExpeditionOrderHandler.cs` — replace `IEshopOrderClient _eshopOrderClient` with
  `IOrderStatusReader`; update the one call site (`GetOrderStatusIdAsync`), including the existing
  `catch (HttpRequestException ex) when (ex.StatusCode == NotFound)` — **the new interface method must
  preserve the ability to throw `HttpRequestException` with `StatusCode` set**, since the handler's 404
  handling depends on that concrete exception shape leaking through the adapter unchanged. Do not wrap or
  translate it into a different exception type in the adapter.
- `ShoptetOrdersModule.cs` — add the two adapter DI registrations (`services.AddTransient<IPackedOrderStatusUpdater, ShoptetOrdersPackedOrderStatusUpdaterAdapter>();`
  and `services.AddTransient<IOrderStatusReader, ShoptetOrdersOrderStatusReaderAdapter>();`), each with the
  same "Cross-module contract: ... DI registration owned by provider" comment style used in
  `ShipmentLabelsModule.cs` / `KnowledgeBaseModule.cs`. Match `IEshopOrderClient`'s own registered lifetime
  for the adapters (check `ShoptetApiAdapterServiceCollectionExtensions.cs` for `IEshopOrderClient`'s
  current lifetime and mirror it, rather than assuming Transient/Scoped).
- `backend/test/Anela.Heblo.Tests/Architecture/ModuleBoundariesTests.cs` — **required, not optional**:
  - Remove the two `IEshopOrderClient` entries from `PackagingShoptetOrdersAllowlist`
    (`ScanPackingOrderHandler -> IEshopOrderClient` and `CompletePackingOrderHandler -> IEshopOrderClient`).
    Leave the `IPackingOrderClient` / `PackingOrder` / `PackingOrderItem` entries untouched — that coupling
    is explicitly out of scope (spec's Out of Scope, and the issue only targets `IEshopOrderClient`).
  - Add a new `ModuleBoundaryRule` for `"ExpeditionList -> ShoptetOrders"`
    (`InspectedNamespacePrefix: "Anela.Heblo.Application.Features.ExpeditionList"`,
    `ForbiddenNamespacePrefixes: ["Anela.Heblo.Application.Features.ShoptetOrders"]`, empty allowlist),
    mirroring the `"ShoptetOrders -> ShipmentLabels"` rule's empty-allowlist-with-explanatory-comment style.
    This closes the gap noted above (no rule currently governs this direction) and is what makes FR-7's
    "no remaining direct coupling" acceptance criterion CI-enforced rather than just true-at-review-time.
  - Update the `PackagingShoptetOrdersAllowlist` doc comment (lines ~292–296) to reflect that it now only
    covers `IPackingOrderClient`, not `IEshopOrderClient`.

### Interfaces and Contracts

```csharp
// Packaging/Contracts/IPackedOrderStatusUpdater.cs
namespace Anela.Heblo.Application.Features.Packaging.Contracts;

public interface IPackedOrderStatusUpdater
{
    Task MarkAsPackedAsync(string orderCode, CancellationToken ct = default);
}
```

```csharp
// ExpeditionList/Contracts/IOrderStatusReader.cs
namespace Anela.Heblo.Application.Features.ExpeditionList.Contracts;

public interface IOrderStatusReader
{
    Task<int> GetOrderStatusIdAsync(string orderCode, CancellationToken ct = default);
}
```

Signatures above are copied verbatim from the current `IEshopOrderClient` members
(`backend/src/Anela.Heblo.Application/Features/ShoptetOrders/IEshopOrderClient.cs:50` and `:6`) — confirmed
by direct source read during this review, not inferred. The developer should still diff against that file
at implementation time in case it has moved since this review.

Both adapters are `internal sealed class` in `ShoptetOrders.Infrastructure`, matching
`ShipmentLabelsShipmentDeliveryCheckerAdapter`'s and `KnowledgeBaseLeafletSourceAdapter`'s visibility —
these are DI-resolved implementation details, never referenced by type outside their own module.

### Data Flow

Unchanged at the integration-with-Shoptet level. Only the in-process call path within the Application layer
changes: `ScanPackingOrderHandler`/`CompletePackingOrderHandler` → `IPackedOrderStatusUpdater` →
`ShoptetOrdersPackedOrderStatusUpdaterAdapter` → `IEshopOrderClient.MarkAsPackedAsync` → (unchanged) Shoptet
adapter → Shoptet API. Same shape for `PrintExpeditionOrderHandler` → `IOrderStatusReader` → adapter →
`IEshopOrderClient.GetOrderStatusIdAsync`.

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| `ModuleBoundariesTests.cs` allowlist edits are forgotten, leaving stale entries that mask the fix's completeness (test still passes either way since removing an unused reference doesn't fail an allowlist check, but the artifact of the old coupling lingers uncleaned) | Low | Explicit task in Implementation Guidance above; FR-7's grep-for-`IEshopOrderClient` acceptance criterion catches the code-side issue independently of the test file |
| `PrintExpeditionOrderHandler`'s 404 handling (`HttpRequestException` with `StatusCode == NotFound`) silently breaks if the adapter wraps/translates the exception | Medium | Called out explicitly above; adapter must be a bare pass-through, not a translation layer — covered by an acceptance criterion the developer should add: a test exercising the 404 path through the new interface |
| Existing unit tests for the three handlers reference `IEshopOrderClient` mocks directly and will fail to compile until updated | Low (expected, not a regression risk) | FR-3/FR-6 in the spec already require updating these tests; this is mechanical (`Mock<IEshopOrderClient>` → `Mock<IPackedOrderStatusUpdater>` / `Mock<IOrderStatusReader>`) since method shapes are unchanged |
| DI lifetime mismatch if adapters are registered with a different lifetime than `IEshopOrderClient` itself, in a way that causes a captive-dependency issue (e.g. adapter Singleton capturing a Scoped `IEshopOrderClient`) | Low | Confirm `IEshopOrderClient`'s registered lifetime in `ShoptetApiAdapterServiceCollectionExtensions.cs` and mirror it for both new adapter registrations |

## Specification Amendments

- **FR-3 / FR-6 acceptance criteria addition:** add an explicit test case verifying the `HttpRequestException`
  / 404 pass-through behavior for `PrintExpeditionOrderHandler` survives through `IOrderStatusReader`
  (see Risks table).
- **FR-7 acceptance criteria addition:** the `ModuleBoundariesTests.cs` changes described above
  (removing the two `IEshopOrderClient` allowlist entries, adding the new `ExpeditionList -> ShoptetOrders`
  rule) are now part of FR-7's "verify no remaining direct coupling" requirement, not a separate nice-to-have —
  without the new rule, nothing in CI would have caught this issue's `PrintExpeditionOrderHandler` violation
  in the first place, and nothing would prevent it recurring.
- **Data Model section:** confirmed no data model impact; no amendment needed there.

## Prerequisites

None. No migrations, config, or infrastructure changes needed — this is a same-PR, same-deploy refactor.
The only sequencing constraint is intra-PR: adapters and DI registration must exist before the handler
constructor changes compile (standard "add the new dependency, then switch to it" ordering within a single
change).
