# Specification: Convert Purchase Module Request DTOs from Record to Class

## Summary
Three MediatR request types in the Purchase module (`GetPurchaseOrderByIdRequest`, `GetPurchaseOrderHistoryRequest`, `UpdatePurchaseOrderStatusRequest`) are declared as C# `record` instead of `class`, violating the repo-wide rule in `CLAUDE.md` that DTOs must be classes because NSwag's OpenAPI client generator mishandles record positional-constructor parameter order. This is a small, mechanical conformance fix: convert all three to classes with settable properties matching the existing pattern already used by the module's other request types, and update the two call sites in `PurchaseOrdersController` that currently construct them positionally.

## Background
An arch-review finding (filed 2026-09-11) identified that these three types deviate from the module's own convention — `CreatePurchaseOrderRequest`, `UpdatePurchaseOrderRequest`, `GetPurchaseOrdersRequest`, and `GetPurchaseStockAnalysisRequest` are already plain classes with property setters. Records use a positional primary constructor; NSwag infers the generated TypeScript client's parameter order from that constructor rather than from JSON property names, which is a latent correctness risk — most acutely for `UpdatePurchaseOrderStatusRequest`, which has two properties (`Id`, `Status`) where a generator mis-ordering would silently swap values at runtime. The two single-property records (`GetPurchaseOrderByIdRequest`, `GetPurchaseOrderHistoryRequest`) are lower risk today but still violate the rule and would become fragile the moment a second property is added.

## Functional Requirements

### FR-1: Convert `GetPurchaseOrderByIdRequest` to a class
Change `backend/src/Anela.Heblo.Application/Features/Purchase/UseCases/GetPurchaseOrderById/GetPurchaseOrderByIdRequest.cs` from a positional `record` to a `class` with a settable `Id` property, preserving the `IRequest<GetPurchaseOrderByIdResponse>` interface.

**Acceptance criteria:**
- `GetPurchaseOrderByIdRequest` is declared as `public class ... : IRequest<GetPurchaseOrderByIdResponse>` with `public int Id { get; set; }`.
- No remaining positional-constructor usage of this type anywhere in the codebase.
- Solution builds with no new warnings/errors attributable to this change.

### FR-2: Convert `GetPurchaseOrderHistoryRequest` to a class
Change `backend/src/Anela.Heblo.Application/Features/Purchase/UseCases/GetPurchaseOrderHistory/GetPurchaseOrderHistoryRequest.cs` from a positional `record` to a `class` with a settable `Id` property, preserving `IRequest<ListResponse<PurchaseOrderHistoryDto>>`.

**Acceptance criteria:**
- `GetPurchaseOrderHistoryRequest` is declared as `public class ... : IRequest<ListResponse<PurchaseOrderHistoryDto>>` with `public int Id { get; set; }`.
- No remaining positional-constructor usage of this type anywhere in the codebase.
- Solution builds with no new warnings/errors attributable to this change.

### FR-3: Convert `UpdatePurchaseOrderStatusRequest` to a class
Change `backend/src/Anela.Heblo.Application/Features/Purchase/UseCases/UpdatePurchaseOrderStatus/UpdatePurchaseOrderStatusRequest.cs` from a positional `record` to a `class` with settable `Id` and `Status` properties, preserving `IRequest<UpdatePurchaseOrderStatusResponse>`.

**Acceptance criteria:**
- `UpdatePurchaseOrderStatusRequest` is declared as `public class ... : IRequest<UpdatePurchaseOrderStatusResponse>` with `public int Id { get; set; }` and `public string Status { get; set; } = null!;` (or the module's established non-nullable-string convention).
- No remaining positional-constructor usage of this type anywhere in the codebase.
- Solution builds with no new warnings/errors attributable to this change.

### FR-4: Update call sites to object-initializer syntax
Update every call site that constructs these three request types positionally — expected in `backend/src/Anela.Heblo.API/Controllers/PurchaseOrdersController.cs`, but confirmed by a full-repo search — to use object-initializer syntax (`new XRequest { Id = id }` etc.) instead of positional arguments.

**Acceptance criteria:**
- A repo-wide search for `new GetPurchaseOrderByIdRequest(`, `new GetPurchaseOrderHistoryRequest(`, and `new UpdatePurchaseOrderStatusRequest(` (positional call syntax) returns no matches after the change.
- Behavior of the affected controller actions is unchanged (same routes, same request/response shapes, same status codes).

## Non-Functional Requirements

### NFR-1: Performance
Not applicable — this is a structural DTO change with no behavioral or performance impact.

### NFR-2: Security
Not applicable — no change to auth, data sensitivity, or input validation.

### NFR-3: Backward compatibility
The public HTTP API surface (routes, request/response JSON shapes) must remain byte-for-byte unchanged. This is an internal C# type change only. The generated OpenAPI/TypeScript client is expected to regenerate cleanly and without shape changes, since property names and JSON serialization are unaffected — only the C#-side constructor/parameter-order risk is eliminated.

## Data Model
No data model changes. `GetPurchaseOrderByIdRequest` and `GetPurchaseOrderHistoryRequest` each carry a single `int Id`. `UpdatePurchaseOrderStatusRequest` carries `int Id` and `string Status`. Response types (`GetPurchaseOrderByIdResponse`, `ListResponse<PurchaseOrderHistoryDto>`, `UpdatePurchaseOrderStatusResponse`) are unaffected and out of scope.

## API / Interface Design
No route, verb, or payload shape changes. The three MediatR requests are internal application-layer types dispatched by `PurchaseOrdersController`; only their C# declaration (`record` → `class`) and their construction syntax at call sites change.

## Dependencies
- MediatR (`IRequest<T>` interface) — unaffected by class-vs-record.
- NSwag OpenAPI client generation — this change is precisely to make generation safer for these types; regenerating the TypeScript client after the change is expected/standard build behavior, not a separate task.

## Out of Scope
- Any other request/response DTOs in the Purchase module or elsewhere, even if similarly non-compliant — this fix is scoped strictly to the three named types and their call sites per the arch-review finding.
- Any refactor of `PurchaseOrdersController` beyond the object-initializer syntax change needed at the three call sites.
- Behavioral changes, new validation, or new endpoints.
- Regenerating/committing the TypeScript OpenAPI client by hand — per project facts, the client is auto-generated on build.

## Open Questions
None.

## Status: COMPLETE
