# Specification: Move `BlockOrderRequest` DTO from API to Application layer

## Summary
The `BlockOrderRequest` DTO currently lives at the bottom of `ShoptetOrdersController.cs` in the API project, violating the architectural rule that the API project must not own DTOs. This specification covers moving the type into the corresponding Application use-case folder so the Application layer becomes the sole owner of the contract, and the controller maps directly without a manual property copy.

## Background
The project follows Clean Architecture with vertical-slice organization under `backend/src/Anela.Heblo.Application/Features/<Module>/UseCases/<UseCase>/`. The standing rule is: **"API project never defines or owns DTOs — it only uses them."** This keeps the API project a thin host layer (routing, model binding, response shaping) and prevents reverse dependencies (Application → API) that would arise if another consumer wanted to reuse a contract.

The current code in `backend/src/Anela.Heblo.API/Controllers/ShoptetOrdersController.cs` (lines 56–60) defines:

```csharp
public class BlockOrderRequest
{
    [JsonPropertyName("note")]
    public string Note { get; set; } = string.Empty;
}
```

The controller's `BlockOrder` action receives `BlockOrderRequest` from the body and manually copies `body.Note` into a separately defined `BlockOrderProcessingRequest` (the MediatR request that already lives in `backend/src/Anela.Heblo.Application/Features/ShoptetOrders/UseCases/BlockOrderProcessing/`). The duplicate contract and manual mapping exist solely because the API-side DTO cannot be referenced from the Application layer.

This finding was filed by the daily arch-review routine on 2026-05-23.

## Functional Requirements

### FR-1: Relocate `BlockOrderRequest` to the Application layer
Move the `BlockOrderRequest` class out of `ShoptetOrdersController.cs` and into the existing `BlockOrderProcessing` use-case folder.

**Target file:**
```
backend/src/Anela.Heblo.Application/Features/ShoptetOrders/UseCases/BlockOrderProcessing/BlockOrderRequest.cs
```

**Target namespace:**
```
Anela.Heblo.Application.Features.ShoptetOrders.UseCases.BlockOrderProcessing
```

**Acceptance criteria:**
- A new file `BlockOrderRequest.cs` exists at the path above containing the moved `BlockOrderRequest` class.
- The class remains a `class` (not a `record`), per the project rule "DTOs are classes, never C# records." (See `CLAUDE.md` / `docs/architecture/development_guidelines.md`.)
- The `[JsonPropertyName("note")]` attribute on the `Note` property is preserved verbatim.
- The `Note` property keeps the same accessibility, type, and default initializer (`public string Note { get; set; } = string.Empty;`).
- The class definition is removed from `ShoptetOrdersController.cs`.

### FR-2: Update the API controller to use the relocated DTO
The controller continues to accept `BlockOrderRequest` from the HTTP body but now references the type from the Application namespace.

**Acceptance criteria:**
- `ShoptetOrdersController.cs` adds (or reuses the existing) `using Anela.Heblo.Application.Features.ShoptetOrders.UseCases.BlockOrderProcessing;` directive — note this `using` is already present in the file.
- The action signature `BlockOrder(string code, [FromBody] BlockOrderRequest body)` continues to compile and bind against the relocated type.
- The unused `using System.Text.Json.Serialization;` at the top of the controller is removed if no other type in the file requires it (it was only there for the moved DTO).
- The manual mapping `Note = body.Note` inside the action body is left as-is for this change. (See "Out of Scope" for the broader question of removing the duplicate contract.)

### FR-3: Preserve HTTP contract and behavior
The relocation must be transparent to API consumers and the OpenAPI schema.

**Acceptance criteria:**
- The HTTP endpoint `PATCH /api/shoptet-orders/{code}/block` continues to accept a JSON body of the shape `{ "note": "..." }`.
- The JSON property name `note` (lowercase) is preserved via the kept `[JsonPropertyName]` attribute.
- Existing controller authorization (`[Authorize]`) and routing attributes are not modified.
- Any generated OpenAPI client signatures for this endpoint remain functionally equivalent (same property name and type). A regenerated TypeScript client must continue to compile against existing call sites.

### FR-4: No business logic changes
This is a pure relocation; no behavior changes.

**Acceptance criteria:**
- `BlockOrderProcessingRequest`, `BlockOrderProcessingResponse`, and `BlockOrderProcessingHandler` are not modified.
- The controller's `HandleResponse(response)` failure path and the `NoContent()` success path are unchanged.
- No new validation, mapping helpers, or abstractions are introduced.

## Non-Functional Requirements

### NFR-1: Build & format
The change must pass the project's standard validation gates.

- `dotnet build` on the backend solution succeeds with no new warnings attributable to this change.
- `dotnet format` reports no diff after the change is applied.

### NFR-2: Tests
Existing tests must continue to pass; no new tests are required for a pure relocation.

- All tests touched by the change must pass.
- If any test currently references `Anela.Heblo.API.Controllers.BlockOrderRequest` (fully qualified), its import is updated to the new namespace. (None expected, but verify via search.)

### NFR-3: Backwards compatibility
No deprecation shim, type alias, or `using` re-export is added. The previous type location is fully removed.

### NFR-4: Architecture compliance
After the change, no DTO is defined inside `backend/src/Anela.Heblo.API/`. A grep for `public class .*Request` or `public class .*Response` under that folder must return no DTO-style results for this endpoint family.

## Data Model
No data-model change. The DTO carries a single `Note` (string) property and binds to the JSON property `note`.

| Type | Property | JSON name | Type | Default |
|---|---|---|---|---|
| `BlockOrderRequest` | `Note` | `note` | `string` | `string.Empty` |

## API / Interface Design

### HTTP endpoint (unchanged)
- **Method:** `PATCH`
- **Route:** `/api/shoptet-orders/{code}/block`
- **Auth:** `[Authorize]` (inherited from controller)
- **Request body:** `BlockOrderRequest` — `{ "note": "<text>" }`
- **Success response:** `204 No Content`
- **Failure response:** Handled by `HandleResponse(response)` when `response.Success == false`.

### Internal flow (unchanged)
Controller receives `BlockOrderRequest` → constructs `BlockOrderProcessingRequest { OrderCode = code, Note = body.Note }` → `IMediator.Send(...)` → handler returns `BlockOrderProcessingResponse`.

### Files affected
| File | Change |
|---|---|
| `backend/src/Anela.Heblo.API/Controllers/ShoptetOrdersController.cs` | Remove `BlockOrderRequest` class (lines 56–60). Remove the `using System.Text.Json.Serialization;` directive if no longer needed in the file. |
| `backend/src/Anela.Heblo.Application/Features/ShoptetOrders/UseCases/BlockOrderProcessing/BlockOrderRequest.cs` | **New file** containing the relocated class with `[JsonPropertyName("note")]` preserved. |

## Dependencies
- The Application project must already reference `System.Text.Json` (it is part of the .NET 8 BCL; no package change needed).
- The API project already has `using Anela.Heblo.Application.Features.ShoptetOrders.UseCases.BlockOrderProcessing;`, so no new `using` is required there.
- OpenAPI client generation pipeline: after the change, a regenerated client should produce the same wire-level contract. Verify by running the generation step locally if applicable.

## Out of Scope
- **Removing the duplicate contract.** Collapsing `BlockOrderRequest` and `BlockOrderProcessingRequest` into a single DTO (e.g., having the controller bind `BlockOrderProcessingRequest` directly, or using a shared body type) is a follow-up consideration and is **not** part of this change. The current two-type design (HTTP DTO + MediatR request) is preserved so this change stays a pure relocation.
- **Renaming, restructuring, or splitting other types** in `ShoptetOrdersController.cs` or the `BlockOrderProcessing` use case.
- **Adding validation attributes** (e.g., `[Required]`, length limits) to `BlockOrderRequest`. Validation behavior remains as-is.
- **Touching `GetPackingOrder` or any other endpoint** in the same controller.
- **Documentation updates** beyond what is necessary for the move itself.

## Open Questions
None.

## Status: COMPLETE