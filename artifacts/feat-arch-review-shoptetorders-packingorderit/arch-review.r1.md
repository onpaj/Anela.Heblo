I have all the context I need. Verified the codebase matches the spec, including filesystem conventions, existing test patterns, and that no frontend consumer reads `weightGrams`.

```markdown
# Architecture Review: Separate Internal `PackingOrderItem` from Public API DTO

## Skip Design: true

Backend refactor with a public-API contract reduction — no UI components, screens, or visual decisions involved. The frontend's hand-written `PackingOrderItem` interface (`useScanPackingOrder.ts:11`) already excludes `weightGrams`, so no rendered surface changes.

## Architectural Fit Assessment

**Excellent fit.** The proposed split exactly applies three rules already in force:

1. **DTO isolation rule** (`docs/architecture/development_guidelines.md` §"Contracts and DTOs Rules"): "DTO objects for API live in `contracts/` of the specific module" and "DTOs are never shared or global." The current `PackingOrderItem` violates both — it is reused as the API DTO across two modules (`ShoptetOrders` and `Packaging`).
2. **Adapter-contract precedent** (same file): `PackingOrder` already carries the wording "Internal contract — not an API DTO" (`IPackingOrderClient.cs:17`). `PackingOrderItem` is the only line type that did not get the same treatment when the contract was tightened — applying it now restores consistency.
3. **OpenAPI class-not-record rule** (`CLAUDE.md`): both new DTOs are classes, matching the rule and matching the existing 60+ DTOs in the codebase (e.g. `ArticleListItemDto`, `JournalEntryDto`, `GiftPackageDto`).

**Integration points:**
- Adapter contract (`IPackingOrderClient`) — unchanged shape, only doc-comment tightened.
- Two handlers (`GetPackingOrderHandler`, `ScanPackingOrderHandler`) — gain one explicit projection each.
- Two response types (`GetPackingOrderResponse`, `ScanOrderData`) — switch element type.
- OpenAPI regeneration cascades to `frontend/src/api/generated/api-client.ts` automatically.

The Packaging-module handlers continue to consume the `IPackingOrderClient` contract owned by ShoptetOrders. This is the documented "Cross-Module Communication" pattern (`development_guidelines.md` §"Cross-Module Communication Example"): the consumer module imports the provider's contract. Nothing changes there.

## Proposed Architecture

### Component Overview

```
                       ┌──────────────────────────────────────────┐
                       │ Adapters/Anela.Heblo.Adapters.ShoptetApi │
                       │ ShoptetApiPackingOrderClient             │
                       │  └─ builds PackingOrder + PackingOrderItem (incl. WeightGrams)
                       └──────────────────────────────────────────┘
                                       │ implements
                                       ▼
        ┌──────────────────────────────────────────────────────────────┐
        │ Application/Features/ShoptetOrders                           │
        │  IPackingOrderClient (contract)                              │
        │  PackingOrder         ← internal Application contract        │
        │  PackingOrderItem     ← internal Application contract        │
        │                         (Name, Quantity, ImageUrl, SetName,  │
        │                          WeightGrams)                        │
        └──────────────────────────────────────────────────────────────┘
              │ consumed by                       │ consumed by
              ▼                                   ▼
 ┌──────────────────────────────┐   ┌──────────────────────────────────┐
 │ ShoptetOrders/UseCases/      │   │ Packaging/UseCases/              │
 │ GetPackingOrder              │   │ ScanPackingOrder + ResetOrderShip │
 │                              │   │                                  │
 │  GetPackingOrderHandler      │   │  ScanPackingOrderHandler         │
 │   projects → PackingOrderItemDto │   │   uses i.WeightGrams (internal)  │
 │                              │   │   projects → ScanPackingOrderItemDto │
 │  GetPackingOrderResponse     │   │                                  │
 │   .Items: List<PackingOrderItemDto>│ │  ScanOrderData               │
 │                              │   │   .Items: List<ScanPackingOrderItemDto>│
 │  ── PUBLIC API ──            │   │  ── PUBLIC API ──                │
 └──────────────────────────────┘   └──────────────────────────────────┘
              │                                   │
              ▼                                   ▼
                  ┌──────────────────────────────────────┐
                  │ OpenAPI Spec ──► api-client.ts       │
                  │ (no weightGrams on either DTO)       │
                  └──────────────────────────────────────┘
```

### Key Design Decisions

#### Decision 1: Two structurally-identical module-local DTOs (not one shared DTO)

**Options considered:**
- (A) One shared `PackingOrderItemDto` in a cross-module location (e.g. `Application/Shared/`).
- (B) Two module-local DTOs (`PackingOrderItemDto` in ShoptetOrders, `ScanPackingOrderItemDto` in Packaging).
- (C) One DTO in ShoptetOrders, re-used from Packaging via cross-module import.

**Chosen approach:** (B). Each owning use case gets its own DTO in its own folder.

**Rationale:** Option (A) is explicitly forbidden ("DTOs are never shared or global", `development_guidelines.md`). Option (C) re-creates the original problem at smaller scale: any field added to satisfy GetPackingOrder leaks into ScanPackingOrder and vice versa, and renames force coordinated changes across modules. The DTOs are intentionally allowed to drift; today they happen to be field-identical, tomorrow ScanPackingOrder may add packaging-specific fields. The minor duplication of four fields buys real module autonomy — the kind of duplication the project rules explicitly accept.

#### Decision 2: DTO location inside the use-case folder, not a `Contracts/` folder

**Options considered:**
- (A) Place `PackingOrderItemDto` in `ShoptetOrders/UseCases/GetPackingOrder/` next to `GetPackingOrderResponse.cs`.
- (B) Place it in `ShoptetOrders/Contracts/` for symmetry with the brief's "or a `Contracts/` folder if the module grows" remark.

**Chosen approach:** (A). DTO lives in the use-case folder.

**Rationale:** `docs/architecture/filesystem.md` distinguishes Simple Features from Complex Features. ShoptetOrders has two use cases (`GetPackingOrder`, `BlockOrderProcessing`); Packaging has five. Neither has a `Contracts/` folder today, and the existing precedent for use-case-local DTOs is strong (`Article/UseCases/ListArticles/ArticleListItemDto.cs`, `Catalog/UseCases/GetProductComposition/IngredientDto.cs`, `Smartsupp/UseCases/ListWebhookAudit/WebhookAuditSummaryDto.cs`). Introducing a `Contracts/` folder for a single DTO would be premature structure. The spec already chose this placement — confirming it as correct.

#### Decision 3: Hand-written projection, no AutoMapper

**Options considered:**
- (A) Inline `Select(i => new PackingOrderItemDto { ... })` in each handler.
- (B) Add a static extension `ToDto(this PackingOrderItem item)` per module.
- (C) Introduce AutoMapper / Mapster.

**Chosen approach:** (A). Inline projection.

**Rationale:** Four fields, two call sites. (B) is a single layer of indirection for zero re-use beyond what (A) gives you. (C) violates the "surgical changes" rule in `CLAUDE.md` and would add a profile, a registration, and a runtime dependency for an O(n) `Select`. (A) is the lightest change that satisfies the requirement.

#### Decision 4: `sealed class` over `class`

**Options considered:**
- (A) `public class PackingOrderItemDto { ... }`.
- (B) `public sealed class PackingOrderItemDto { ... }`.

**Chosen approach:** (B) `sealed class`.

**Rationale:** Every use-case-local DTO in the codebase that I found uses `sealed` (e.g. `ArticleListItemDto.cs:5`). The spec says "public classes with public get/set properties" without specifying `sealed`. Match the codebase precedent.

## Implementation Guidance

### Directory / Module Structure

Two new files, no folder changes:

```
backend/src/Anela.Heblo.Application/Features/
├── ShoptetOrders/
│   ├── IPackingOrderClient.cs                    (edit: tighten XML doc on PackingOrderItem)
│   └── UseCases/GetPackingOrder/
│       ├── GetPackingOrderHandler.cs             (edit: project Items)
│       ├── GetPackingOrderResponse.cs            (edit: change List<T>)
│       └── PackingOrderItemDto.cs                ← NEW
└── Packaging/UseCases/ScanPackingOrder/
    ├── ScanPackingOrderHandler.cs                (edit: project Items)
    ├── ScanPackingOrderResponse.cs               (edit: change List<T>, drop unused using)
    └── ScanPackingOrderItemDto.cs                ← NEW

backend/test/Anela.Heblo.Tests/Application/
├── ShoptetOrders/GetPackingOrderHandlerTests.cs  (edit + add reflection assertion)
└── Packaging/ScanPackingOrderHandlerTests.cs     (edit + add reflection assertion)
```

`ResetOrderShipmentHandler.cs` is **not** touched — it never returns `PackingOrderItem` in its response (`ResetOrderShipmentResponse` uses `ResetShipmentData`/`ResetShipmentPackage`). It only reads `i.WeightGrams` from the internal contract for its own weight math (`ResetOrderShipmentHandler.cs:58`). That stays valid.

### Interfaces and Contracts

**New (both files):**
```csharp
namespace Anela.Heblo.Application.Features.ShoptetOrders.UseCases.GetPackingOrder;

public sealed class PackingOrderItemDto
{
    public string Name { get; set; } = string.Empty;
    public int Quantity { get; set; }
    public string? ImageUrl { get; set; }
    public string? SetName { get; set; }
}
```

```csharp
namespace Anela.Heblo.Application.Features.Packaging.UseCases.ScanPackingOrder;

public sealed class ScanPackingOrderItemDto
{
    public string Name { get; set; } = string.Empty;
    public int Quantity { get; set; }
    public string? ImageUrl { get; set; }
    public string? SetName { get; set; }
}
```

**Internal contract (existing, doc tightened):**
```csharp
/// <summary>A single line on a packing order. Internal contract — not an API DTO.</summary>
public class PackingOrderItem
{
    public string Name { get; set; } = string.Empty;
    public int Quantity { get; set; }
    public string? ImageUrl { get; set; }
    public string? SetName { get; set; }
    public int WeightGrams { get; set; }
}
```

### Data Flow

**GET packing order (read path):**
```
HTTP GET → Controller → GetPackingOrderRequest
  → GetPackingOrderHandler
      → IPackingOrderClient.GetPackingOrderAsync()  [ShoptetApi adapter builds PackingOrder + PackingOrderItem incl. WeightGrams]
      → Project: order.Items.Select(i => new PackingOrderItemDto { Name, Quantity, ImageUrl, SetName }).ToList()
      → GetPackingOrderResponse { Items: List<PackingOrderItemDto> }    ← WeightGrams dropped at projection boundary
  → JSON serialization → HTTP
```

**POST scan packing order (write path):**
```
HTTP POST → Controller → ScanPackingOrderRequest
  → ScanPackingOrderHandler
      → IPackingOrderClient.GetPackingOrderAsync()  [PackingOrderItem incl. WeightGrams]
      → uses order.Items[i].WeightGrams * Quantity for shipment weight (internal calc, unchanged)
      → Project: order.Items.Select(i => new ScanPackingOrderItemDto { Name, Quantity, ImageUrl, SetName }).ToList()
      → ScanOrderData { Items: List<ScanPackingOrderItemDto> }          ← WeightGrams dropped at projection boundary
      → carrier + label work continues
  → ScanPackingOrderResponse → JSON → HTTP
```

The weight-calculation logic in `ScanPackingOrderHandler.cs:102` (`order.Items.Sum(i => i.WeightGrams * i.Quantity)`) continues to read from `order.Items` (internal `PackingOrderItem`) — not from `orderData.Items` (now DTO). The projection happens once, into `orderData.Items`, and is independent of the math. Verify ordering in the edited handler: build `orderData` last (after weight math), or be careful to project into a local first.

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| Future developer adds field to internal `PackingOrderItem` expecting it to flow through to API (the very mistake this fixes) | High | FR-7 reflection tests fail CI if `WeightGrams` (or any property beyond the four) appears on either DTO. Keep test wording explicit so the failure message points at the cause. |
| Backward-compat break: a client reads `.weightGrams` from the JSON response | Low | Verified: no consumer in `frontend/src/` reads `.weightGrams` outside the generated client. Hand-written FE interface (`useScanPackingOrder.ts:11`) already omits it. OQ-1 in the spec is therefore resolved as "safe to remove." |
| `ScanPackingOrderHandler` projection placed before the weight calculation reads from the wrong collection | Medium | Implementation guidance: place the `.ToList()` projection into `orderData.Items` only after, or independent of, the weight math at line 102. The existing test that exercises the weight-eligibility path (`MinPackageWeightGrams = 100`) covers this — its expectation must continue to pass. |
| OpenAPI client regeneration not picked up by local dev | Low | `docs/development/api-client-generation.md` states the client is auto-regenerated on backend build. Spec requires `npm run build` + `npm run lint` to pass — that catches a stale client. |
| Other tests construct `PackingOrderItem` to set up the `IPackingOrderClient` mock and would not need changes | Low | Confirmed: `ResetOrderShipmentHandlerTests.cs:37`, `CreateOrderShipmentHandlerTests.cs:36`, `ScanPackingOrderHandlerPackagePersistenceTests.cs:77` use `PackingOrderItem` only on the mock-input side — they are unaffected. Spec FR-7 bullet 3 already calls this out. |
| Cross-module coupling between Packaging and ShoptetOrders is reinforced (Packaging keeps importing `PackingOrderItem`) | Low (accepted) | Already the existing arrangement and documented as the cross-module pattern. Not in scope; future audit (per spec "Out of Scope" §2) may address whether Packaging should own its own minimal adapter contract. |

## Specification Amendments

**Amendment 1 (cosmetic):** Use `sealed class` rather than `class` for both new DTOs, matching the precedent in `ArticleListItemDto.cs:5`, `IngredientDto.cs`, `WebhookAuditSummaryDto.cs`, and every other use-case-local DTO inspected. Update FR-1's acceptance criteria to read "Both are public **sealed** classes with public get/set properties."

**Amendment 2 (clarification):** FR-4's instruction for `ScanPackingOrderHandler` should explicitly state: *the projection into `ScanPackingOrderItemDto` must use `order.Items` (the internal collection), and must not replace the internal collection used by the weight calculation at line 102.* As worded, the spec implies this; making it explicit prevents an accidental "single mutation" implementation that breaks weight math.

**Amendment 3 (resolves OQ-1, which the spec already lists as `None` but the NFR-3 paragraph leaves implicit):** A grep over `frontend/src/**/*.ts{,x}` excluding `generated/api-client.ts` returned zero matches for `weightGrams` or `WeightGrams`. The removal is safe. Add an explicit one-line note to NFR-3: "Verified 2026-06-05: no consumer of `weightGrams` exists in the frontend source outside the generated client."

**Amendment 4 (test-hygiene):** Add a fourth required test in FR-7: a reflection assertion that `PackingOrderItem` (the internal type) still **does** have `WeightGrams` — this anchors the symmetric guarantee and would catch an over-eager future cleanup that drops it from the internal contract and silently regresses the shipment weight calc.

No structural amendments. The spec is sound.

## Prerequisites

None. This change is purely additive (two new files) plus four small edits and test updates. No DB migration, no config, no infrastructure work, no new packages, no new DI registrations. Build-time OpenAPI regeneration is already wired (`docs/development/api-client-generation.md`).

Validation gates per `CLAUDE.md`: `dotnet build` + `dotnet format`, all touched tests pass under `dotnet test`, `npm run build` + `npm run lint` in `frontend/`. E2E suite is nightly and out-of-band — not required for this change.
```