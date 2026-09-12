# Architecture Review: Convert Purchase Module Request DTOs from Record to Class

## Skip Design: true

## Architectural Fit Assessment
This is a pure conformance fix, not a new architectural capability. The Purchase module's MediatR request types already establish the target pattern consistently: `CreatePurchaseOrderRequest`, `UpdatePurchaseOrderRequest`, `GetPurchaseOrdersRequest`, `GetPurchaseStockAnalysisRequest`, and `UpdatePurchaseOrderInvoiceAcquiredRequest` are all `public class ... : IRequest<T>` with `{ get; set; }` properties (verified by reading each file). The three types in scope are the only outliers in the module — declared as positional `record`s. No new component, dependency, or module boundary is introduced; this brings three files into line with an already-dominant, already-correct in-module convention plus the repo-wide `CLAUDE.md` rule ("DTOs are classes, never C# records").

## Proposed Architecture

### Component Overview
No structural change. All three types stay in their existing Vertical Slice folders under `Features/Purchase/UseCases/{UseCase}/`, keep their existing `IRequest<TResponse>` contracts, and keep their existing handlers untouched.

```
PurchaseOrdersController ──dispatches──> IMediator.Send(request)
        │                                        │
        ├─ GetPurchaseOrderById/GetPurchaseOrderByIdRequest        (record -> class)
        ├─ GetPurchaseOrderHistory/GetPurchaseOrderHistoryRequest  (record -> class)
        └─ UpdatePurchaseOrderStatus/UpdatePurchaseOrderStatusRequest (record -> class)
```

### Key Design Decisions

#### Decision 1: Class shape — plain class with auto-properties, no record features retained
**Options considered:**
- Keep `record` but switch to a parameterless primary constructor / init-only property record (`record { public int Id { get; init; } }`) — still a record, does not satisfy the CLAUDE.md rule ("classes, never records"), and NSwag's generator behavior for record-with-object-initializer-syntax is exactly the ambiguous case the rule exists to avoid. Rejected.
- Convert to `class` with `{ get; set; }` properties, matching `CreatePurchaseOrderRequest`, `UpdatePurchaseOrderRequest`, `GetPurchaseOrdersRequest`, `GetPurchaseStockAnalysisRequest`, `UpdatePurchaseOrderInvoiceAcquiredRequest`. Chosen.

**Chosen approach:** Plain `class` with mutable auto-properties (`{ get; set; }`), exactly mirroring the sibling request types already in this module. No `[Required]`/`[Range]` validation attributes are added beyond what the record had (the record had none) — do not introduce new validation as part of this fix; that would be scope creep beyond the arch-review finding.

**Rationale:** Consistency with the dominant in-module pattern (5 of 8 request types in `Features/Purchase/UseCases/` already use this exact shape) and direct compliance with the repo-wide rule. Using `{ get; set; }` rather than `{ get; init; }` matches sibling classes exactly (verified in `CreatePurchaseOrderRequest.cs`, `GetPurchaseStockAnalysisRequest.cs`) and is required for NSwag/ASP.NET model binding via `[FromBody]` on `UpdatePurchaseOrderStatusRequest`, which needs a settable property, not just object-initializer support.

#### Decision 2: `Status` property nullability annotation on `UpdatePurchaseOrderStatusRequest`
**Options considered:**
- `public string Status { get; set; } = null!;` (non-null-forgiving default, matches `CreatePurchaseOrderRequest.OrderDate` pattern for required strings).
- `public string? Status { get; set; }` (nullable, would require null-checks downstream).

**Chosen approach:** `public string Status { get; set; } = null!;` — matches the existing convention for required string properties elsewhere in the module (e.g. `CreatePurchaseOrderRequest.OrderDate`, `CreatePurchaseOrderLineRequest.MaterialId`).

**Rationale:** The record version had no nullable annotation on `string Status` (i.e., was implicitly non-nullable, required via the positional constructor). The `= null!` pattern preserves "required, non-null" semantics without introducing a compile-time nullable-reference warning, exactly as done elsewhere in this module.

## Implementation Guidance

### Directory / Module Structure
No new files or directories. Edit in place:
- `backend/src/Anela.Heblo.Application/Features/Purchase/UseCases/GetPurchaseOrderById/GetPurchaseOrderByIdRequest.cs`
- `backend/src/Anela.Heblo.Application/Features/Purchase/UseCases/GetPurchaseOrderHistory/GetPurchaseOrderHistoryRequest.cs`
- `backend/src/Anela.Heblo.Application/Features/Purchase/UseCases/UpdatePurchaseOrderStatus/UpdatePurchaseOrderStatusRequest.cs`

### Interfaces and Contracts
Target shapes (verified against sibling classes and existing consumers):

```csharp
// GetPurchaseOrderByIdRequest.cs
public class GetPurchaseOrderByIdRequest : IRequest<GetPurchaseOrderByIdResponse>
{
    public int Id { get; set; }
}

// GetPurchaseOrderHistoryRequest.cs
public class GetPurchaseOrderHistoryRequest : IRequest<ListResponse<PurchaseOrderHistoryDto>>
{
    public int Id { get; set; }
}

// UpdatePurchaseOrderStatusRequest.cs
public class UpdatePurchaseOrderStatusRequest : IRequest<UpdatePurchaseOrderStatusResponse>
{
    public int Id { get; set; }
    public string Status { get; set; } = null!;
}
```

Handlers (`GetPurchaseOrderByIdHandler`, `GetPurchaseOrderHistoryHandler`, `UpdatePurchaseOrderStatusHandler`) reference these types only by property access (`request.Id`, `request.Status`) — confirm this while implementing, but no handler-signature changes are expected since MediatR dispatches by type, not by constructor shape.

### Data Flow
Unchanged. `PurchaseOrdersController` still receives route/body parameters and dispatches via `IMediator.Send(...)`; only the object-construction syntax at two controller call sites changes, and `UpdatePurchaseOrderStatusRequest` continues to be model-bound directly from the request body via `[FromBody]` (this binding is in fact the primary motivation for the fix — ASP.NET/NSwag-generated client round-tripping is safer against a class with settable properties than a positional record).

**All call sites requiring updates (verified via full-repo search for positional-constructor usage of these three types), not just the controller:**

1. `backend/src/Anela.Heblo.API/Controllers/PurchaseOrdersController.cs:65` — `new GetPurchaseOrderByIdRequest(id)` → `new GetPurchaseOrderByIdRequest { Id = id }`
2. `backend/src/Anela.Heblo.API/Controllers/PurchaseOrdersController.cs:140` — `new GetPurchaseOrderHistoryRequest(id)` → `new GetPurchaseOrderHistoryRequest { Id = id }`
   - Note: `UpdatePurchaseOrderStatusRequest` has **no positional-construction call site in the controller** — it arrives via `[FromBody]` model binding at line 98 (`[FromBody] UpdatePurchaseOrderStatusRequest request`), so there is no controller-side construction to change for it. The brief's statement "update the call sites in `PurchaseOrdersController`" applies fully to the two `Get*` requests only.
3. `backend/test/Anela.Heblo.Tests/Features/Purchase/UpdatePurchaseOrderStatusHandlerTests.cs` — **14 occurrences** of `new UpdatePurchaseOrderStatusRequest(ValidOrderId, "...")` (lines 42, 72, 99, 117, 138, 158, 183, 222, 243, 265, 287, 314, 342, 361) → `new UpdatePurchaseOrderStatusRequest { Id = ValidOrderId, Status = "..." }`
4. `backend/test/Anela.Heblo.Tests/Features/Purchase/GetPurchaseOrderHistoryHandlerTests.cs` — 4 occurrences of `new GetPurchaseOrderHistoryRequest(id)` (lines 32, 52, 75, 98) → `new GetPurchaseOrderHistoryRequest { Id = id }`
5. `backend/test/Anela.Heblo.Tests/Controllers/PurchaseOrdersControllerTests.cs` — 4 occurrences of `new UpdatePurchaseOrderStatusRequest(orderId, "...")` (lines 332, 363, 373, 400) → object-initializer form

Converting the record declarations without updating (3)-(5) leaves the test project non-compiling — this is a build-breaking dependency, not optional cleanup. It is in scope because it is required for `dotnet build` to succeed, matching the spec's FR-4 acceptance criterion ("no remaining positional-constructor usage of this type anywhere in the codebase") and this repo's validation-before-completion rule (`dotnet build` must pass).

## Risks and Mitigations
| Risk | Severity | Mitigation |
|------|----------|------------|
| Test files using positional-constructor syntax fail to compile after the record→class conversion | Medium | Update all 22 test call sites identified above in the same change; `dotnet build` (including the test project) must be run before declaring done, per this repo's validation rules. |
| Missed call site elsewhere in the codebase (e.g. a script, another controller, an integration test project) | Low | Re-run a repo-wide grep for `new GetPurchaseOrderByIdRequest(`, `new GetPurchaseOrderHistoryRequest(`, and `new UpdatePurchaseOrderStatusRequest(` with an opening paren immediately after the type name, after the edit, to confirm zero remaining positional-construction matches. |
| NSwag-generated TypeScript client output changes shape (e.g. property order/casing) after regeneration | Low | Property names and JSON shape are unaffected by record→class; only the C#-side constructor disappears. Regeneration is automatic on build per project facts — no manual client edits needed. Spot-check the regenerated client's request types after `npm run build` if desired, but no shape change is expected. |
| `UpdatePurchaseOrderStatusRequest.Status` losing implicit non-null enforcement now that it is a settable property instead of a required constructor argument | Low | `= null!;` preserves the "expected non-null" contract for static analysis purposes; the handler's own validation (visible in the referenced test file's "InvalidStatus" cases) already validates the value, so runtime behavior for a missing/invalid status is unchanged. |

## Specification Amendments
Amend FR-4 in `spec.r1.md` to clarify:
- Only `GetPurchaseOrderByIdRequest` and `GetPurchaseOrderHistoryRequest` have positional-construction call sites inside `PurchaseOrdersController` itself; `UpdatePurchaseOrderStatusRequest` is bound via `[FromBody]` and has no controller-side construction to change.
- The full set of call sites requiring updates also includes three test files (`UpdatePurchaseOrderStatusHandlerTests.cs`, `GetPurchaseOrderHistoryHandlerTests.cs`, `PurchaseOrdersControllerTests.cs`, 22 occurrences total) — these are in scope because leaving them unconverted breaks the build, not as a scope expansion.

## Prerequisites
None. No migrations, config, or infrastructure changes are needed — this is a same-commit, backend-only source edit.
