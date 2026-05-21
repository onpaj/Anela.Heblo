# Architecture Review: Consistent Not-Found Error Handling in PackingMaterials Module

## Skip Design: true

## Architectural Fit Assessment

The proposal aligns cleanly with patterns already proven in the same module. The allocation handlers (`GetAllocations`, `CreateAllocation`, `UpdateAllocation`, `DeleteAllocation`) are the de-facto reference implementation: they all return responses extending `Anela.Heblo.Application.Shared.BaseResponse`, which already exposes `Success` and `ErrorCode` plus a localization `Params` dictionary. Each response additionally defines its own `Error` string property. The PackingMaterials controller already encodes the inverse mapping for allocations at `PackingMaterialsController.cs:166-235` using `NotFound(new { error = response.Error })`. The proposed change applies that exact recipe to four more handlers — no new shared infrastructure required.

Two integration points need closer attention than the spec implies:

1. **`DeletePackingMaterialRequest` uses `IRequest` (Unit), not `IRequest<TResponse>`.** Its handler is `IRequestHandler<DeletePackingMaterialRequest>` returning bare `Task`, and there is no `DeletePackingMaterialResponse.cs` file. Switching to a structured return requires changing the request contract, the handler signature, and creating a new response type. The other three handlers already return typed responses extending `BaseResponse`, so only their bodies change.
2. **The controller envelope for not-found is `NotFound(new { error = response.Error })`, not `NotFound(response)`.** The spec's FR-6 example is wrong; it must match the allocation pattern verbatim to keep wire-format uniform.

`ErrorCodes.ResourceNotFound` is already attributed `[HttpStatusCode(HttpStatusCode.NotFound)]`, so the enum side requires no change.

## Proposed Architecture

### Component Overview

```
┌─────────────────────────────────────────────────────────────┐
│ PackingMaterialsController (Anela.Heblo.API)                │
│                                                             │
│   PUT  /{id}                  POST /{id}/quantity           │
│   DEL  /{id}                  GET  /{id}/logs               │
│                                                             │
│   ┌─ inspect response.Success ──┐                           │
│   │  ErrorCode == ResourceNotFound → NotFound(new { error })│
│   │  Success == true            → Ok / NoContent           │
│   │  else                       → BadRequest(new { error })│
│   └─────────────────────────────┘                           │
└──────────────────────────┬──────────────────────────────────┘
                           │ MediatR
                           ▼
┌─────────────────────────────────────────────────────────────┐
│ Use-case handlers (Anela.Heblo.Application/...)             │
│   UpdatePackingMaterialHandler                              │
│   UpdatePackingMaterialQuantityHandler                      │
│   DeletePackingMaterialHandler                              │
│   GetPackingMaterialLogsHandler                             │
│                                                             │
│   material is null → return Response {                      │
│     Success = false,                                        │
│     ErrorCode = ErrorCodes.ResourceNotFound,                │
│     Error = "Packing material {id} not found."              │
│   }                                                         │
└──────────────────────────┬──────────────────────────────────┘
                           │
                           ▼
                IPackingMaterialRepository
```

### Key Design Decisions

#### Decision 1: Add `Error` property per response, reuse `Success`/`ErrorCode` from `BaseResponse`
**Options considered:**
- (A) Add `Error` to each affected response only (mirrors `GetAllocationsResponse`, `CreateAllocationResponse`).
- (B) Lift `Error` into `BaseResponse` so every response in the codebase gets it.

**Chosen approach:** A.
**Rationale:** Option B is a cross-cutting change touching every response across all modules and is explicitly out of scope. The current convention — `Success`/`ErrorCode`/`Params` on `BaseResponse`, `Error` free-text on individual responses — is what the allocation handlers and the controller wiring already follow. Stay surgical.

#### Decision 2: Convert `DeletePackingMaterialRequest` to `IRequest<DeletePackingMaterialResponse>`
**Options considered:**
- (A) Keep `IRequest` (Unit) and use a try/catch in the controller mapping the exception to 404.
- (B) Promote to `IRequest<DeletePackingMaterialResponse>`, create the response DTO, change handler signature.

**Chosen approach:** B.
**Rationale:** A reintroduces the throw-and-catch pattern this spec exists to eliminate and is inconsistent with the three sibling handlers and the allocation handlers. B is one extra file (`DeletePackingMaterialResponse.cs`) plus a signature change — small, mechanical, and uniform.

#### Decision 3: Controller error envelope is `NotFound(new { error = response.Error })`, not `NotFound(response)`
**Options considered:**
- (A) `NotFound(response)` — serializes the whole response DTO, including `Success`, `ErrorCode`, and any payload fields (which would be null/default on the not-found path).
- (B) `NotFound(new { error = response.Error })` — mirrors the allocation endpoints.

**Chosen approach:** B.
**Rationale:** The four allocation endpoints in the same controller already use B. Uniformity beats theoretical merit. The spec's example must be overridden here.

#### Decision 4: Do not add try/catch wrappers in the four handlers
**Options considered:**
- (A) Wrap the handler body in try/catch and translate unexpected exceptions to a structured `{ Success = false, Error = "..." }` response, mirroring `GetAllocationsHandler` and `CreateAllocationHandler`.
- (B) Leave unexpected exceptions to propagate to the global error handler (HTTP 500); only the not-found path is structured.

**Chosen approach:** B for this PR.
**Rationale:** The spec explicitly scopes "not-found case only" and excludes other error conditions. Adding catch-all wrappers expands scope and starts a debate (logging level, what counts as "user-displayable error", etc.) better addressed module-wide in a separate ticket. Note this as a follow-up — see Specification Amendments.

## Implementation Guidance

### Directory / Module Structure

Files to **edit** (no new directories):
- `backend/src/Anela.Heblo.Application/Features/PackingMaterials/UseCases/UpdatePackingMaterial/UpdatePackingMaterialHandler.cs`
- `backend/src/Anela.Heblo.Application/Features/PackingMaterials/UseCases/UpdatePackingMaterialQuantity/UpdatePackingMaterialQuantityHandler.cs`
- `backend/src/Anela.Heblo.Application/Features/PackingMaterials/UseCases/DeletePackingMaterial/DeletePackingMaterialHandler.cs`
- `backend/src/Anela.Heblo.Application/Features/PackingMaterials/UseCases/DeletePackingMaterial/DeletePackingMaterialRequest.cs`
- `backend/src/Anela.Heblo.Application/Features/PackingMaterials/UseCases/GetPackingMaterialLogs/GetPackingMaterialLogsHandler.cs`
- `backend/src/Anela.Heblo.Application/Features/PackingMaterials/Contracts/UpdatePackingMaterialRequest.cs` (contains `UpdatePackingMaterialResponse` inline — add `Error`)
- `backend/src/Anela.Heblo.Application/Features/PackingMaterials/Contracts/UpdatePackingMaterialQuantityResponse.cs` (add `Error`)
- `backend/src/Anela.Heblo.Application/Features/PackingMaterials/UseCases/GetPackingMaterialLogs/GetPackingMaterialLogsResponse.cs` (add `Error`)
- `backend/src/Anela.Heblo.API/Controllers/PackingMaterialsController.cs` (four endpoints)

Files to **create**:
- `backend/src/Anela.Heblo.Application/Features/PackingMaterials/UseCases/DeletePackingMaterial/DeletePackingMaterialResponse.cs`

Tests (add to existing test project, mirroring `AllocationHandlerTests.cs`):
- `backend/test/Anela.Heblo.Tests/Features/PackingMaterials/PackingMaterialCrudHandlerTests.cs` (or split per-handler — match existing style; the module currently uses a single per-area `*HandlerTests.cs` file).
- Reuse `MockPackingMaterialRepository` already used by `AllocationHandlerTests.cs`.

### Interfaces and Contracts

**New response (delete):**
```csharp
namespace Anela.Heblo.Application.Features.PackingMaterials.UseCases.DeletePackingMaterial;

public class DeletePackingMaterialResponse : BaseResponse
{
    public string? Error { get; set; }
}
```

**Request signature change (delete):**
```csharp
public class DeletePackingMaterialRequest : IRequest<DeletePackingMaterialResponse>
{
    public int Id { get; set; }
}
```

**Existing response extensions (additive — `Success`, `ErrorCode`, `Params` already come from `BaseResponse`):**

```csharp
// UpdatePackingMaterialResponse, UpdatePackingMaterialQuantityResponse,
// GetPackingMaterialLogsResponse — each gains:
public string? Error { get; set; }
```

The payload properties (`Material`, `Logs`) must remain. On the not-found path the handler returns a response with `Success = false`, the `Error` string set, and the payload property left as default (`null!` for `Material`, default for `Logs`). Callers MUST inspect `Success` before reading payload properties — controller does this; frontend currently throws on non-OK status before touching the body, so it is safe.

**Handler return pattern (uniform across all four):**
```csharp
if (material == null)
{
    return new TResponse
    {
        Success = false,
        ErrorCode = ErrorCodes.ResourceNotFound,
        Error = $"Packing material with ID {request.Id} not found."
    };
}
```

Use the exact message template `$"Packing material with ID {id} not found."` — this matches the existing allocation handler messages (`GetAllocationsHandler.cs:32`, `CreateAllocationHandler.cs:38`). This resolves FR-7 in favor of the existing convention rather than the spec's variant `"Packing material {id} not found."`.

**Controller mapping pattern (uniform across all four endpoints):**
```csharp
var response = await _mediator.Send(request, cancellationToken);
if (response.Success) return Ok(response);        // or NoContent() for Delete
if (response.ErrorCode == ErrorCodes.ResourceNotFound)
    return NotFound(new { error = response.Error });
return BadRequest(new { error = response.Error });
```

For `DELETE /{id}`, the success branch is `NoContent()`. For `PUT /{id}`, `POST /{id}/quantity`, `GET /{id}/logs`, the success branch is `Ok(response)`.

Add `[ProducesResponseType]` attributes to mirror the allocation endpoints (e.g. `200`, `404`).

### Data Flow

**Not-found path (e.g. `PUT /api/packing-materials/99` where 99 does not exist):**
1. Controller dispatches `UpdatePackingMaterialRequest { Id = 99 }` via MediatR.
2. `UpdatePackingMaterialHandler` calls `_repository.GetByIdAsync(99, ct)` → returns `null`.
3. Handler returns `UpdatePackingMaterialResponse { Success = false, ErrorCode = ResourceNotFound, Error = "Packing material with ID 99 not found." }`.
4. Controller inspects `response.Success == false`, checks `ErrorCode == ResourceNotFound`, returns `NotFound(new { error = response.Error })`.
5. Client receives HTTP 404 with body `{ "error": "Packing material with ID 99 not found." }`.

**Happy path:** unchanged. Handler sets `Success` implicitly via `BaseResponse` default (`true` in the parameterless constructor). Controller returns `Ok(response)` / `NoContent()`.

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| Frontend hand-rolled `PackingMaterialsApiClient` (`frontend/src/api/hooks/usePackingMaterials.ts`) throws `new Error("API request failed: ${statusText}")` on any non-OK status. The behavior is identical for 404 and 500 — UI shows a generic error. | Low | No code change required. Document the audit result in the PR description per FR-9. If product wants distinct UX for "material not found", that is a separate ticket. |
| `DeletePackingMaterialResponse` is a new type — OpenAPI client regeneration introduces a new generated TS type. | Low | Run `dotnet build` then `npm run build` to regenerate the client. Verify the generated client compiles (`api-client.ts` will gain the new type). Frontend currently invokes `deletePackingMaterial` via the hand-rolled `makeRequest<void>` and ignores the body — additive type does not break call sites. |
| The four handlers under change do **not** have try/catch wrappers; unexpected exceptions still hit the global handler as HTTP 500. Mixed pattern with allocation handlers (which do wrap) remains. | Low | Out of scope. File a follow-up to align all PackingMaterials handlers' unexpected-exception treatment. Do not expand scope here. |
| Tests for the four handlers do not exist today (only `AllocationHandlerTests.cs` exists for this module). FR-8 mandates new tests, and existing happy-path coverage for these handlers is also missing. | Medium | Add tests for both the not-found path (the FR) AND the happy path (so we don't merge untested code). Use `MockPackingMaterialRepository` from the existing test file. |
| `UpdatePackingMaterialResponse` is declared inline in `UpdatePackingMaterialRequest.cs` (lines 15-18), unlike the others which are in separate files. Editing requires modifying that combined file. | Low | Edit in place. Do not move to a separate file in this PR (out of surgical scope). |
| Empty `Material`/`Logs` payload fields on not-found responses serialize as `null` / default. If a future consumer assumes payload presence, it crashes. | Low | Mitigated by `Success` flag — controller redirects to 404 before the body is consumed for the success path. Frontend throws before reading. |
| The `Params` localization dictionary on `BaseResponse` is unused by the allocation pattern (uses free-text `Error` instead). This PR continues that convention; if future i18n work requires `Params`, it will need to be reconciled module-wide. | Low | Acknowledge in PR description; do not introduce in this change. |

## Specification Amendments

1. **FR-6, controller envelope:** Override the spec's `NotFound(response)` with `NotFound(new { error = response.Error })`. The allocation controller uses the latter (see `PackingMaterialsController.cs:168, 191, 218, 233`). The non-success-non-not-found fallback is `BadRequest(new { error = response.Error })`, matching the same convention.

2. **FR-7, message template:** The spec proposes `"Packing material {id} not found."`. The existing allocation handlers use `"Packing material with ID {id} not found."` (see `GetAllocationsHandler.cs:32`, `CreateAllocationHandler.cs:38`). Adopt the existing wording verbatim across all six handlers — uniformity over the spec's alternative phrasing.

3. **FR-3, Delete contract change:** The spec implicitly assumes `DeletePackingMaterialResponse` exists. It does not. The PR must:
   - Create `DeletePackingMaterialResponse.cs` (extending `BaseResponse`, with `Error` string).
   - Change `DeletePackingMaterialRequest` from `IRequest` to `IRequest<DeletePackingMaterialResponse>`.
   - Change `DeletePackingMaterialHandler` to `IRequestHandler<DeletePackingMaterialRequest, DeletePackingMaterialResponse>` returning `Task<DeletePackingMaterialResponse>`.
   - Change the controller's `DeletePackingMaterial` action to capture the response, branch on `Success`, and return `NoContent()` on success.

4. **NFR-5, observability:** The four handlers currently have no logger. Continue without injecting one in this PR — the spec lists this as acceptable, and the 404 response itself is observable in HTTP request logs. Do not add `ILogger` injection just for the not-found path; it would be a different change from the reference pattern (the four allocation handlers inject `ILogger` only because they have try/catch wrappers).

5. **FR-9, frontend audit:** Confirmed audit result (record in PR description): the only call sites are in `frontend/src/api/hooks/usePackingMaterials.ts`, which throws a generic `Error` on any non-OK response. No call site distinguishes 404 from 500. No frontend code change is required. Generated client regeneration is automatic on backend build.

6. **Happy-path success flag:** Affected handlers do not currently set `Success`. `BaseResponse`'s parameterless constructor sets it to `true`, so the happy path is correct by default. Do not add explicit `Success = true` assignments — they would be noise and inconsistent with the allocation success returns.

## Prerequisites

None. No migrations, no infrastructure, no new packages, no new shared types. All required building blocks (`BaseResponse`, `ErrorCodes.ResourceNotFound`, `[HttpStatusCode]` attribute mapping, controller-side envelope convention, MediatR dispatch) already exist. The OpenAPI TypeScript client regenerates automatically on `dotnet build`.