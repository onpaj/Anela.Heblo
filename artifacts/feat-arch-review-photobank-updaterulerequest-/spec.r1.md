# Specification: Fix UpdateRule Endpoint Body Contract

## Summary
The `PhotobankController.UpdateRule` endpoint exposes a misleading API contract by using the internal MediatR `UpdateRuleRequest` type as its `[FromBody]` parameter, causing an `id` field to appear in the OpenAPI schema and auto-generated TypeScript client even though the controller silently overrides it with the route value. This spec introduces a dedicated `UpdateRuleBody` contract class containing only fields the client is allowed to control, aligning with the rest of the Photobank module's contract pattern.

## Background
The Photobank module follows a Clean Architecture / Vertical Slice pattern where MediatR request types (commands/queries) are internal handler inputs and `contracts/*Body.cs` types are the public HTTP body contracts. The arch-review on 2026-06-14 found that `PUT /api/photobank/settings/rules/{id}` violates this convention:

- `UpdateRuleRequest` (a MediatR command with `public int Id { get; set; }`) is bound directly as `[FromBody]`.
- The OpenAPI generator includes `id` in the request body schema.
- The auto-generated TypeScript client requires (or at minimum allows) callers to set `id` in the JSON payload.
- The controller manually reconstructs the command, taking `Id` from the route parameter and ignoring the body's `Id`.

The result is a deceptive contract: callers can set body `id` to any value with no effect, creating a debugging trap and a documentation inconsistency. Every other write endpoint in the module already uses a dedicated `contracts/*Body.cs` type, so this is an outlier.

The fix is mechanical and behavior-preserving — the only observable change is that `id` disappears from the request body's OpenAPI schema (and from the regenerated TypeScript client type).

## Functional Requirements

### FR-1: Introduce `UpdateRuleBody` contract
Create a new class `UpdateRuleBody` under the Photobank module's `contracts/` folder (same folder/namespace convention as other `*Body` types in the module).

The class must contain exactly the fields the client is permitted to control:
- `PathPattern` (string, required — `null!` initializer matching module convention)
- `TagName` (string, required — `null!` initializer matching module convention)
- `IsActive` (bool)
- `SortOrder` (int)

The class must **not** declare an `Id` field.

The class must be a regular `class`, not a C# `record` (per project DTO rule — `docs/architecture/development_guidelines.md`).

**Acceptance criteria:**
- File `UpdateRuleBody.cs` exists in the Photobank module's `contracts/` folder.
- The class is `public`, declares the four properties above with public getters and setters, and contains no other properties.
- The class is not a `record`.
- It lives in the same namespace as the other `*Body` types in `contracts/`.

### FR-2: Update `PhotobankController.UpdateRule` to bind `UpdateRuleBody`
Change the `[FromBody]` parameter type from `UpdateRuleRequest` to `UpdateRuleBody`. Construct the internal `UpdateRuleRequest` MediatR command from the route `id` and the body fields, preserving the existing "route value wins" behavior.

**Acceptance criteria:**
- `PhotobankController.UpdateRule` signature uses `[FromBody] UpdateRuleBody body` (parameter name may be `body` or equivalent — match local convention).
- The handler still constructs `new UpdateRuleRequest { Id = id, PathPattern = body.PathPattern, TagName = body.TagName, IsActive = body.IsActive, SortOrder = body.SortOrder }`.
- The dispatched MediatR command and the returned `ActionResult<UpdateRuleResponse>` are unchanged.
- No other endpoints, MediatR handlers, validators, or business logic are modified.
- `UpdateRuleRequest` itself is unchanged — `Id` remains on the MediatR command (it is the handler's input contract).

### FR-3: Regenerate OpenAPI clients
After the controller change, regenerate the C# and TypeScript OpenAPI clients (per `docs/development/api-client-generation.md`). The regenerated artifacts must reflect the new body schema.

**Acceptance criteria:**
- The OpenAPI schema for `PUT /api/photobank/settings/rules/{id}` no longer lists `id` as a body property.
- The regenerated TypeScript client's `updateRule` method body type contains only `pathPattern`, `tagName`, `isActive`, `sortOrder`.
- Frontend code that previously sent `id` in the body (if any — see FR-4) compiles successfully against the regenerated client.

### FR-4: Frontend caller adjustments
Search the frontend for callers of the generated `updateRule` client method. If any caller currently passes `id` inside the body payload, remove it (the route parameter is supplied separately). If no callers pass `id` in the body, no frontend change is required.

**Acceptance criteria:**
- `npm run build` and `npm run lint` pass in `frontend/`.
- Any updated caller passes the new body shape without an `id` field.
- No new TypeScript compile errors are introduced by the regenerated client.

### FR-5: Behavior preservation
The endpoint's runtime behavior must be identical before and after the change for any valid request. The change is a contract cleanup only.

**Acceptance criteria:**
- Existing unit/integration tests for `UpdateRule` pass without modification (or with only mechanical updates to construct `UpdateRuleBody` instead of `UpdateRuleRequest` in test arrange blocks).
- A request with the new body shape and a route `id` returns the same `UpdateRuleResponse` as the equivalent pre-change request.
- Validation messages, status codes, and error envelopes are unchanged.

## Non-Functional Requirements

### NFR-1: Backwards compatibility (wire-level)
The HTTP wire contract remains backwards-compatible for existing live clients: a JSON body that still includes an extra `id` field will be ignored by ASP.NET model binding (extra properties are not rejected by default), and the route `id` continues to drive the update. The change is therefore non-breaking at the HTTP level; only the *advertised* schema and generated client *types* change.

### NFR-2: Consistency with module conventions
The change must align `UpdateRule` with the existing `contracts/*Body.cs` pattern used by every other write endpoint in the Photobank module. The new file's namespace, accessibility, property-initializer style (`= null!` for non-nullable references), and folder location must match neighboring `*Body` types.

### NFR-3: Build & format gates
Before declaring the task done:
- `dotnet build` succeeds.
- `dotnet format` reports no changes (or is run and committed).
- `npm run build` and `npm run lint` succeed in `frontend/`.
- All touched tests pass.

### NFR-4: No security/authn changes
The endpoint's existing authentication and authorization attributes (whatever they currently are) are preserved verbatim. This change does not touch the auth surface.

## Data Model
No database schema changes. The change is purely at the HTTP contract layer.

**New type (HTTP contract):**

| Type | Field | C# type | Notes |
|---|---|---|---|
| `UpdateRuleBody` | `PathPattern` | `string` | Required; `= null!` initializer |
| `UpdateRuleBody` | `TagName` | `string` | Required; `= null!` initializer |
| `UpdateRuleBody` | `IsActive` | `bool` | |
| `UpdateRuleBody` | `SortOrder` | `int` | |

**Unchanged internal type:**

`UpdateRuleRequest` (MediatR command) keeps its current shape including `Id`. It is constructed inside the controller from `(routeId, body)`.

## API / Interface Design

**Endpoint:** `PUT /api/photobank/settings/rules/{id}` (unchanged route, method, response shape)

**Before (request body schema, abbreviated):**
```json
{
  "id": 0,
  "pathPattern": "string",
  "tagName": "string",
  "isActive": true,
  "sortOrder": 0
}
```

**After (request body schema, abbreviated):**
```json
{
  "pathPattern": "string",
  "tagName": "string",
  "isActive": true,
  "sortOrder": 0
}
```

**Response:** `UpdateRuleResponse` — unchanged.

**Controller (target shape):**
```csharp
[HttpPut("settings/rules/{id:int}")]
public async Task<ActionResult<UpdateRuleResponse>> UpdateRule(
    int id,
    [FromBody] UpdateRuleBody body,
    CancellationToken cancellationToken = default)
{
    var command = new UpdateRuleRequest
    {
        Id = id,
        PathPattern = body.PathPattern,
        TagName = body.TagName,
        IsActive = body.IsActive,
        SortOrder = body.SortOrder,
    };
    var result = await _mediator.Send(command, cancellationToken);
    return Ok(result);
}
```
(Exact attribute syntax, mediator field name, and return statement should match the current controller — only the parameter type and command construction's field-list source change.)

## Dependencies
- **OpenAPI client generation pipeline** — the C# and TypeScript clients regenerate on build per `docs/development/api-client-generation.md`. No tooling change required.
- **Existing `UpdateRuleRequest` MediatR handler** — must continue to receive `Id` populated from the route. No handler-side change.
- **Frontend TypeScript client consumers** of `updateRule` — may need a mechanical edit if they pass `id` in the body today.

No new external libraries, services, or infrastructure are introduced.

## Out of Scope
- **Auditing other endpoints** in the Photobank module (or other modules) for the same anti-pattern. If the daily arch-review surfaces more, those will be filed as separate items.
- **Refactoring `UpdateRuleRequest`** — removing `Id` from the MediatR command is *not* in scope; the command is an internal handler input and other call sites (if any) may depend on its shape.
- **Validation rule changes** — FluentValidation (or equivalent) rules for `UpdateRuleRequest` are preserved; if validators currently target `UpdateRuleRequest`, they continue to apply after construction in the controller.
- **Route or HTTP-method changes** to the endpoint.
- **Response shape changes** to `UpdateRuleResponse`.
- **Database, persistence, or domain model changes.**
- **E2E test additions** — the nightly E2E suite already exercises the endpoint via the frontend; no new E2E tests are required for this contract cleanup.

## Open Questions
None.

## Status: COMPLETE