# Architecture Review: Move `BlockOrderRequest` DTO from API to Application layer

## Skip Design: true

Backend-only relocation with no UI/UX work, no new components, no visual changes.

## Architectural Fit Assessment

The proposal aligns precisely with the project's stated rule in `docs/architecture/development_guidelines.md` (and `CLAUDE.md`): **"`API` project never defines or owns DTOs – it only uses them."** It also matches the codebase's actual file layout conventions verified during review:

- The Application layer uses Vertical Slice organization under `Features/<Module>/UseCases/<UseCase>/`.
- Sibling use cases (e.g. `GetOrderShipmentLabels`, `CreateOrderShipment`, `GenerateLeaflet`) already collocate `*Request.cs` / `*Response.cs` / `*Handler.cs` in the same use-case folder.
- The MediatR companion `BlockOrderProcessingRequest.cs` already lives in the target folder; adding the HTTP DTO next to it requires no new conventions.

Integration points are minimal and well-bounded: a single controller action (`PATCH /api/shoptet-orders/{code}/block`), a single MediatR pipeline (handler unchanged), and a single OpenAPI client surface (TS class `BlockOrderRequest`, JSON property `note`). No cross-module dependency, no new DI registration, no schema impact.

A secondary observation: `ShipmentLabelsController.cs` exhibits the same anti-pattern (`GetShipmentLabelsRequest` defined at the bottom of the controller). **Explicitly out of scope here**, but worth noting for a follow-up arch-review entry rather than scope-creeping this change.

## Proposed Architecture

### Component Overview

```
HTTP Client
   │  PATCH /api/shoptet-orders/{code}/block  { "note": "..." }
   ▼
┌────────────────────────────────────────────────────────────────┐
│ Anela.Heblo.API                                                │
│  ShoptetOrdersController.BlockOrder(code, BlockOrderRequest)   │
│    │                                                           │
│    │   uses (no longer owns)                                   │
│    ▼                                                           │
└────┼───────────────────────────────────────────────────────────┘
     │
     ▼
┌────────────────────────────────────────────────────────────────┐
│ Anela.Heblo.Application.Features.ShoptetOrders.UseCases        │
│   .BlockOrderProcessing                                        │
│      • BlockOrderRequest        ← NEW LOCATION (HTTP body DTO) │
│      • BlockOrderProcessingRequest  (MediatR IRequest)         │
│      • BlockOrderProcessingResponse                            │
│      • BlockOrderProcessingHandler                             │
└────────────────────────────────────────────────────────────────┘
```

Ownership lines after change: Application owns both the HTTP-shape DTO and the internal MediatR contract. API contains routing + binding + mapping only.

### Key Design Decisions

#### Decision 1: Where in the Application layer the DTO lives
**Options considered:**
- `Features/ShoptetOrders/Contracts/BlockOrderRequest.cs` (per the table in `development_guidelines.md`).
- `Features/ShoptetOrders/UseCases/BlockOrderProcessing/BlockOrderRequest.cs` (per the spec, matching sibling use cases).

**Chosen approach:** Use-case folder, as the spec proposes.

**Rationale:** The MediatR request and handler for this exact use case already live there, and every comparable use case in the repo (ShipmentLabels, GenerateLeaflet, etc.) keeps `*Request.cs` colocated in the use-case folder rather than promoted to a module-wide `Contracts/`. The `Contracts/` folder in this codebase is in practice used for cross-use-case shared DTOs (e.g. `ShipmentLabelDto`, `CatalogPurchaseRecordDto`), not for one-off HTTP request bodies tied to a single use case. Vertical-slice cohesion wins.

#### Decision 2: Keep `BlockOrderRequest` distinct from `BlockOrderProcessingRequest`
**Options considered:**
- Collapse into a single class (bind `BlockOrderProcessingRequest` directly in the controller).
- Preserve the two-type design (HTTP DTO + MediatR request) and only relocate.

**Chosen approach:** Preserve, as spec mandates.

**Rationale:** The collapse is tempting but introduces semantic coupling (MediatR contract becomes the wire contract; adding a non-HTTP field to the MediatR request would leak into OpenAPI). The split also keeps the route parameter (`OrderCode` from URL) cleanly separated from the body (`Note`). Scope-discipline applies — the spec is correct to defer this to a follow-up.

#### Decision 3: Class vs. record
**Chosen approach:** `class`, unchanged.

**Rationale:** Project-wide rule from `CLAUDE.md`: "DTOs are classes, never C# records" because NSwag's OpenAPI generator mishandles record positional parameters. Non-negotiable.

## Implementation Guidance

### Directory / Module Structure

**New file:**
```
backend/src/Anela.Heblo.Application/Features/ShoptetOrders/UseCases/BlockOrderProcessing/BlockOrderRequest.cs
```

**Modified file:**
```
backend/src/Anela.Heblo.API/Controllers/ShoptetOrdersController.cs
```

### Interfaces and Contracts

**New file `BlockOrderRequest.cs` — exact content:**

```csharp
using System.Text.Json.Serialization;

namespace Anela.Heblo.Application.Features.ShoptetOrders.UseCases.BlockOrderProcessing;

public class BlockOrderRequest
{
    [JsonPropertyName("note")]
    public string Note { get; set; } = string.Empty;
}
```

**Controller changes — exact:**

1. Delete lines 56–60 (the inline `BlockOrderRequest` class).
2. Delete the `using System.Text.Json.Serialization;` directive on line 6 — it is no longer referenced by any type defined in the controller file.
3. **Do not add** a new `using` for `Anela.Heblo.Application.Features.ShoptetOrders.UseCases.BlockOrderProcessing;` — it is already present on line 1.
4. The `BlockOrder` action body remains identical, including the `Note = body.Note` mapping into `BlockOrderProcessingRequest`.

### Data Flow

Unchanged. The relocation is purely lexical:

```
HTTP body { "note": "..." }
  → ASP.NET model binder → BlockOrderRequest (Application namespace)
  → controller maps Note → BlockOrderProcessingRequest { OrderCode, Note }
  → IMediator.Send → BlockOrderProcessingHandler
  → BlockOrderProcessingResponse → 204 NoContent | HandleResponse(failure)
```

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| OpenAPI schema name collision: both the moved `BlockOrderRequest` (DTO) and `BlockOrderProcessingRequest` (MediatR request) get serialized into the Swagger schema. NSwag uses short type names — same as before the move, so no new collision is introduced, but verify the generated TS client diffs. | LOW | Run the OpenAPI client regeneration step (`dotnet build` triggers it via project target) and `git diff frontend/src/api/generated/api-client.ts` — expect zero meaningful changes. If the schema name changes, namespace-qualify via NSwag's `SchemaNameGenerator` settings; do not rename the C# class. |
| Forgetting to remove `using System.Text.Json.Serialization;` leaves a dead import that `dotnet format` may not flag (it warns rather than errors for unused usings in some configs). | LOW | Spec lists it explicitly. Verify with `dotnet build` warnings and a final `grep -n "System.Text.Json.Serialization" ShoptetOrdersController.cs` returning nothing. |
| `JsonPropertyName` attribute lost in the move would silently change the wire contract from `note` to `Note` (PascalCase), breaking existing FE callers. | HIGH | Acceptance criterion FR-3 + a manual diff of the generated TS client (`BlockOrderRequest` should still serialize `note`). Optional smoke check: POST to `/swagger` schema and grep for `"note"`. |
| Frontend generated client (`frontend/src/api/generated/api-client.ts`) regenerates with a different class signature, breaking any call sites that import `BlockOrderRequest`. | LOW | The TS class is generated from the C# type's **short name**, not its namespace; the move does not change the wire shape. Verify by regenerating and grepping call sites — there is at least one usage at `api-client.ts:9835`. |
| Two similarly-named types (`BlockOrderRequest` and `BlockOrderProcessingRequest`) now live in the same C# namespace, increasing IntelliSense noise and risk of mis-import in future edits. | LOW | Accepted as the cost of keeping the relocation pure (per spec "Out of Scope"). Flag for the follow-up that collapses the duplicate contract. |
| Hidden references to `Anela.Heblo.API.Controllers.BlockOrderRequest` (fully qualified) elsewhere in the solution would fail to compile after the move. | LOW | Grep across the solution confirms only two references, both inside `ShoptetOrdersController.cs` itself (line 30 binding, line 56 definition). Tests (`BlockOrderProcessingHandlerTests.cs`, `BlockOrderProcessingIntegrationTests.cs`) reference only the MediatR request, not the HTTP DTO. No external impact. |

## Specification Amendments

None required. The spec is correct, scoped, and verifiable. Two minor clarifications to fold in if convenient (not blockers):

1. **NFR-1 wording**: state explicitly that the generated TS client at `frontend/src/api/generated/api-client.ts` must show no wire-level diff after regeneration. The spec implies this under FR-3; making it a checked acceptance criterion under NFR-1 closes a gap.
2. **Verification grep for FR-1**: add an explicit check that `grep -n "class BlockOrderRequest" backend/src/Anela.Heblo.API/` returns no matches after the change — concrete and trivially scriptable.

## Prerequisites

None. This change requires no migrations, no configuration, no new packages, no infrastructure work, and no coordination with other workstreams. It can be implemented and merged independently. After merge:

- A fresh `dotnet build` regenerates the OpenAPI client.
- `dotnet format` should produce no diff.
- Existing tests cover the unchanged handler path; no new tests are required.