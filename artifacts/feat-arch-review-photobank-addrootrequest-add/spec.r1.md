# Specification: Decouple Photobank MediatR Requests from HTTP Body DTOs

## Summary
Three `PhotobankController` actions (`AddRoot`, `AddRule`, `RetagPhotos`) bind incoming HTTP bodies directly to MediatR request types living under `UseCases/`, breaking the module's established `contracts/` pattern. This work introduces slim `*Body` DTOs in `contracts/` for each endpoint, maps them to the corresponding MediatR requests in the controller, and aligns the three endpoints with the four endpoints in the same controller that already follow the convention.

## Background
The Photobank module follows the project-wide rule from `docs/architecture/development_guidelines.md`: *"DTO objects for API (Request, Response) live in `contracts/` of the specific module."* The intent is to keep the public HTTP surface decoupled from internal application contracts so that:

- Server-controlled fields (e.g. `CreatedByUserId` populated by the handler) cannot be spoofed from the client because they never appear on the HTTP body DTO.
- OpenAPI schema and the auto-generated TypeScript client describe only what the API actually accepts.
- Changes to handler-internal state do not silently reshape the public API contract.

Four endpoints in `PhotobankController` already follow the pattern correctly (`AddPhotoTagBody → AddPhotoTagRequest`, `BulkAddPhotoTagBody → BulkAddPhotoTagRequest`, `CreateTagBody → CreateTagRequest`, `BulkAddPhotoTagByIdsBody → BulkAddPhotoTagByIdsRequest`). The three offending endpoints were filed by the daily arch-review routine on 2026-06-14:

| Controller action | `[FromBody]` type used | Where it lives |
|---|---|---|
| `AddRoot` (line 233) | `AddRootRequest` | `UseCases/AddRoot/AddRootRequest.cs` |
| `AddRule` (line 281) | `AddRuleRequest` | `UseCases/AddRule/AddRuleRequest.cs` |
| `RetagPhotos` (line 203) | `RetagPhotosRequest` | `UseCases/RetagPhotos/RetagPhotosRequest.cs` |

## Functional Requirements

### FR-1: Introduce `AddRootBody` contract DTO
Add a class `AddRootBody` under the Photobank module's `contracts/` directory that mirrors only the client-supplied fields of `AddRootRequest`. The class must be a plain C# class (not a record) per the project DTO rule, and properties must match the existing JSON shape consumed by the frontend.

**Acceptance criteria:**
- File `contracts/AddRootBody.cs` exists in the Photobank module under the same namespace pattern used by existing `*Body` DTOs in the module.
- `AddRootBody` is a class (not a record).
- Property names, types, nullability, and default values match the JSON wire shape currently produced by `AddRootRequest` for client-bound fields (`SharePointPath`, `DisplayName`, `DriveId`, plus any other client-facing field present today).
- No server-populated fields (e.g. user identity, audit metadata) appear on `AddRootBody`.
- The class compiles and is referenced by the controller (see FR-4).

### FR-2: Introduce `AddRuleBody` contract DTO
Add a class `AddRuleBody` under `contracts/` mirroring the client-supplied fields of `AddRuleRequest`.

**Acceptance criteria:**
- File `contracts/AddRuleBody.cs` exists in the Photobank module.
- `AddRuleBody` is a class (not a record).
- Property names, types, nullability, and default values match the JSON wire shape currently produced by `AddRuleRequest` for client-bound fields (`PathPattern`, `TagName`, `SortOrder`, plus any other client-facing field present today).
- No server-populated fields appear on `AddRuleBody`.

### FR-3: Introduce `RetagPhotosBody` contract DTO
Add a class `RetagPhotosBody` under `contracts/` mirroring the client-supplied fields of `RetagPhotosRequest`.

**Acceptance criteria:**
- File `contracts/RetagPhotosBody.cs` exists in the Photobank module.
- `RetagPhotosBody` is a class (not a record).
- Property names, types, nullability, and default values match the JSON wire shape currently produced by `RetagPhotosRequest` for client-bound fields (`PhotoIds`, `ClearExistingAiTags`, plus any other client-facing field present today).
- Array/collection defaults preserve the current wire behavior (e.g. `PhotoIds` defaults to an empty array, not null).
- No server-populated fields appear on `RetagPhotosBody`.

### FR-4: Update `PhotobankController` to map `Body` → `Request`
Change the three controller actions to bind `[FromBody]` to the new `*Body` types and construct the MediatR `*Request` instance inside the action, matching the established pattern used by `AddPhotoTagBody → AddPhotoTagRequest` in the same file.

**Acceptance criteria:**
- `AddRoot` action signature uses `[FromBody] AddRootBody body` and sends a `new AddRootRequest { ... }` populated from `body` to `IMediator`.
- `AddRule` action signature uses `[FromBody] AddRuleBody body` and sends a `new AddRuleRequest { ... }` populated from `body`.
- `RetagPhotos` action signature uses `[FromBody] RetagPhotosBody body` and sends a `new RetagPhotosRequest { ... }` populated from `body`.
- The mapping pattern (e.g. object initializer style, helper method, etc.) matches whatever pattern is already used by the four existing well-formed endpoints in the same controller for consistency.
- No client-facing field on the existing requests is dropped during mapping — the wire contract stays identical.
- HTTP route, verb, status codes, response type, attribute filters (auth, model validation, etc.), and action name remain unchanged.

### FR-5: Regenerate API clients and verify wire compatibility
The C# and TypeScript OpenAPI clients must be regenerated and the generated request schemas for the three endpoints must remain wire-compatible with what the frontend currently sends. The generated TypeScript types may legitimately change name (e.g. `AddRootRequest` → `AddRootBody`), but the JSON shape must not.

**Acceptance criteria:**
- `dotnet build` passes (which triggers the OpenAPI TypeScript client regeneration per project setup).
- The regenerated OpenAPI schema for `/AddRoot`, `/AddRule`, and `/RetagPhotos` request bodies has the same property names, types, and required flags as before this change.
- Any frontend call site that referenced the old generated type name is updated to use the new generated type name. No call site changes its payload contents.
- `npm run build` and `npm run lint` pass in the frontend.

### FR-6: Backend test coverage
Existing tests covering the three endpoints continue to pass without modification of their input payloads. Where tests directly instantiate `AddRootRequest` / `AddRuleRequest` / `RetagPhotosRequest` to exercise the handler, they may stay unchanged because handlers still accept the same MediatR request types. Where tests exercise the HTTP layer (controller integration tests), they must use the new `*Body` types or raw JSON that matches the unchanged wire shape.

**Acceptance criteria:**
- All previously passing backend tests in the Photobank module remain green.
- Controller-level tests for the three endpoints exist or are added so that the `Body → Request` mapping is covered (a single test per endpoint verifying field-by-field copy is sufficient).
- `dotnet build` and `dotnet format` succeed.

## Non-Functional Requirements

### NFR-1: Performance
No performance impact expected. The change is a thin object-to-object copy in the controller layer. Allocation cost is negligible relative to the MediatR pipeline and downstream I/O. No new async work, no extra DB round-trips, no extra serialization.

### NFR-2: Security
The change closes a latent over-exposure risk by ensuring that any future server-populated field added to a MediatR request type cannot be set by a malicious client. Specifically:

- No field on `AddRootBody`, `AddRuleBody`, or `RetagPhotosBody` may represent server-controlled state (identity, audit timestamps, derived authorization context, etc.).
- The mapping in the controller must construct the MediatR request such that server-populated fields are set from the server context, not from the body.
- No authentication, authorization, or model-validation behavior changes for these endpoints.

### NFR-3: Backward compatibility
The HTTP wire contract must not break. Existing frontend builds talking to a backend with this change must continue to function until the frontend is rebuilt against the regenerated client. Property names, types, nullability, and required flags in the request JSON schema must remain identical for all three endpoints.

### NFR-4: Maintainability
The three updated endpoints must visually match the existing well-formed endpoints in `PhotobankController` so the file presents one consistent pattern. Naming convention is `{ActionName}Body` for the contract DTO, matching the existing convention in the module.

## Data Model
No persistence changes. No database schema changes. No domain entity changes.

New transient API contract types in the Photobank module:

- `AddRootBody` — client-supplied fields for adding a Photobank root.
- `AddRuleBody` — client-supplied fields for adding a tagging rule.
- `RetagPhotosBody` — client-supplied fields for triggering a retag operation.

Each is a plain C# class living under the Photobank module's `contracts/` directory, mapped 1:1 to the equivalent client-facing fields of the existing MediatR request under `UseCases/`. Existing MediatR request types (`AddRootRequest`, `AddRuleRequest`, `RetagPhotosRequest`) are retained in `UseCases/` and may freely gain server-populated fields in the future without leaking into the API.

## API / Interface Design

### Endpoints (unchanged routes, unchanged verbs, unchanged status codes)

- `AddRoot` — request body shape unchanged on the wire; bound to `AddRootBody` internally; mapped to `AddRootRequest` before dispatch.
- `AddRule` — request body shape unchanged on the wire; bound to `AddRuleBody` internally; mapped to `AddRuleRequest` before dispatch.
- `RetagPhotos` — request body shape unchanged on the wire; bound to `RetagPhotosBody` internally; mapped to `RetagPhotosRequest` before dispatch.

### Controller pattern (target)

```csharp
public async Task<IActionResult> AddRoot([FromBody] AddRootBody body, CancellationToken ct)
{
    var request = new AddRootRequest
    {
        SharePointPath = body.SharePointPath,
        DisplayName = body.DisplayName,
        DriveId = body.DriveId,
    };
    var result = await _mediator.Send(request, ct);
    return Ok(result);
}
```

The exact shape (method signature, return type, attributes) must mirror the existing well-formed endpoints (`AddPhotoTag`, `BulkAddPhotoTag`, `CreateTag`, `BulkAddPhotoTagByIds`) in the same controller.

### Generated TypeScript client

The auto-generated client will expose the new type names (`AddRootBody`, `AddRuleBody`, `RetagPhotosBody`) instead of the old MediatR request names. Frontend call sites that referenced the old names must be updated to the new names; payload contents are identical.

## Dependencies

- MediatR — already in use; no version change.
- OpenAPI TypeScript client generator — already wired into the build; runs on `dotnet build`.
- Existing `PhotobankController` and existing handlers for `AddRoot`, `AddRule`, `RetagPhotos` — modified only at the controller boundary; handlers untouched.

No new external services, libraries, NuGet packages, or npm packages are introduced.

## Out of Scope

- Refactoring or renaming the existing MediatR request types under `UseCases/`. They keep their current names and locations.
- Changing handler logic, validation rules, or business behavior of `AddRoot`, `AddRule`, or `RetagPhotos`.
- Auditing the rest of the codebase for similar coupling outside the Photobank module. Other modules may have the same issue but are filed separately by the daily arch-review routine.
- Adding new server-populated fields (e.g. `CreatedByUserId`) to the MediatR requests. This spec only enables that change to be made safely later.
- Database, persistence, or domain entity changes.
- UX, design, or layout changes in the frontend.
- E2E test changes — the wire contract is unchanged, so existing E2E coverage applies as-is.

## Open Questions

None.

## Status: COMPLETE