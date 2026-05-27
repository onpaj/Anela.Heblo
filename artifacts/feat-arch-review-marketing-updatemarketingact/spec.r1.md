# Specification: Extract Shared MarketingFolderLinkRequest DTO

## Summary
Refactor the marketing action contracts to eliminate an Interface Segregation violation where `UpdateMarketingActionRequest` depends on a nested type owned by `CreateMarketingActionRequest`. Extract `CreateFolderLinkRequest` into a standalone, shared contract class (`MarketingFolderLinkRequest`) referenced by both Create and Update DTOs.

## Background
The marketing module exposes two independent use-case contracts: `CreateMarketingActionRequest` and `UpdateMarketingActionRequest`. Today, the Update contract reuses the Create contract's nested `CreateFolderLinkRequest` class:

```csharp
// UpdateMarketingActionRequest.cs:30
public List<CreateMarketingActionRequest.CreateFolderLinkRequest>? FolderLinks { get; set; }
```

This creates an invisible, compile-time coupling between two contracts that should evolve independently. A creation-only field added to `CreateFolderLinkRequest` (for example, an initial-state flag or owner-on-create attribute) would silently leak into the Update API surface. The naming is also misleading: a request to update something carries a type prefixed `Create…`.

Both contracts already share the same shape for folder links — a `FolderKey` string and a `FolderType` enum — so a shared DTO is the natural model. This refactor closes the ISP violation, removes the confusing naming, and creates one obvious place to evolve the folder-link contract going forward.

Per the project rule in `CLAUDE.md` and `docs/architecture/development_guidelines.md`, all request/response DTOs are classes (never C# records) because the OpenAPI client generator mishandles record parameter order. The new shared class must follow this convention.

## Functional Requirements

### FR-1: Introduce shared `MarketingFolderLinkRequest` contract
Create a new public class `MarketingFolderLinkRequest` in `backend/src/Anela.Heblo.Application/Features/Marketing/Contracts/`, in its own file `MarketingFolderLinkRequest.cs`. The class must carry the exact same properties and validation attributes that `CreateMarketingActionRequest.CreateFolderLinkRequest` carries today, with no additions or removals.

**Acceptance criteria:**
- File `backend/src/Anela.Heblo.Application/Features/Marketing/Contracts/MarketingFolderLinkRequest.cs` exists.
- The type is a plain `class` (not a `record`), in namespace `Anela.Heblo.Application.Features.Marketing.Contracts`.
- It declares `public string FolderKey { get; set; } = null!;` decorated with `[Required]` and `[MaxLength(100)]`, matching the original property exactly.
- It declares `public MarketingFolderType FolderType { get; set; }` decorated with `[Required]`, matching the original property exactly.
- No additional members are introduced beyond what existed on the nested class.

### FR-2: Update `CreateMarketingActionRequest` to use the shared DTO
Remove the nested `CreateFolderLinkRequest` declaration from `CreateMarketingActionRequest` and retype the `FolderLinks` collection to reference the new shared class.

**Acceptance criteria:**
- The nested class `CreateFolderLinkRequest` no longer exists inside `CreateMarketingActionRequest`.
- `CreateMarketingActionRequest.FolderLinks` is typed as `List<MarketingFolderLinkRequest>?` (or `List<MarketingFolderLinkRequest>` if the original was non-nullable — preserve original nullability and any validation attributes).
- All existing validation attributes on `FolderLinks` (e.g. `[Required]`, `[MinLength]`) remain unchanged.

### FR-3: Update `UpdateMarketingActionRequest` to use the shared DTO
Retype `UpdateMarketingActionRequest.FolderLinks` to reference the new shared class.

**Acceptance criteria:**
- `UpdateMarketingActionRequest.FolderLinks` is typed as `List<MarketingFolderLinkRequest>?`, preserving the original nullability and attribute set.
- The file no longer references `CreateMarketingActionRequest.CreateFolderLinkRequest`.

### FR-4: Update all consumers of the old nested type
Locate every call site, handler, mapper, validator, and test that references `CreateMarketingActionRequest.CreateFolderLinkRequest` and migrate it to `MarketingFolderLinkRequest`. Typical locations include the Create handler, the Update handler, AutoMapper / manual mapping profiles, FluentValidation validators (if any), and unit/integration tests under `backend/test/`.

**Acceptance criteria:**
- A repository-wide search for `CreateFolderLinkRequest` returns zero matches after the change.
- A repository-wide search for `CreateMarketingActionRequest.CreateFolderLinkRequest` returns zero matches after the change.
- Handlers and mappers compile against `MarketingFolderLinkRequest` with no behavioral changes.

### FR-5: Regenerate OpenAPI / TypeScript clients and propagate the rename to the frontend
The OpenAPI TypeScript client is auto-generated on build (`docs/development/api-client-generation.md`). After the backend rename, the generated client will expose the shared type under the new name. Any frontend code currently importing or using the old `CreateFolderLinkRequest` type must be updated to import `MarketingFolderLinkRequest`.

**Acceptance criteria:**
- `npm run build` regenerates the TypeScript client and emits no type errors.
- A repository-wide search under `frontend/src/` for `CreateFolderLinkRequest` returns zero matches.
- `npm run lint` passes.
- Frontend marketing-action create and edit screens still compile and continue to send `folderLinks` in the same JSON shape as before.

### FR-6: Preserve serialized JSON wire format
The on-the-wire JSON for both create and update requests must remain byte-for-byte compatible with the current contract. Property names, casing, ordering hints, and nested object shape stay identical; only the .NET type name changes.

**Acceptance criteria:**
- A request body that successfully created or updated a marketing action before the refactor succeeds after the refactor with no payload changes.
- The OpenAPI schema diff shows the folder-link object schema as a `$ref` to a renamed component, with identical properties, types, and required flags. No property is added, removed, or renamed.

## Non-Functional Requirements

### NFR-1: Behavior preservation
The refactor is type-only. Validation behavior, handler logic, persistence, and any side effects must be unchanged. No new feature is introduced; no existing feature is altered.

### NFR-2: Build and lint cleanliness
After the change:
- `dotnet build` succeeds with zero new warnings.
- `dotnet format` reports no diffs.
- `npm run build` succeeds.
- `npm run lint` succeeds.

### NFR-3: Test coverage
All existing unit and integration tests for marketing action Create and Update flows must continue to pass without behavioral edits. Tests that referenced the old nested type are mechanically updated to the new type with no assertion changes. If existing coverage does not exercise the `FolderLinks` field on both Create and Update, add at least one round-trip test per use case that constructs a request with a populated `FolderLinks` list and asserts the handler accepts it — sufficient to keep total touched-code coverage at or above the project's 80% baseline.

### NFR-4: Backwards-compatible naming
The new class is named `MarketingFolderLinkRequest` (module-prefixed, suffix `Request`) to match the existing naming convention in the Marketing Contracts folder and to make the shared nature obvious. This naming is binding.

## Data Model
No database schema, entity, or domain-model change. The change is confined to the application-layer contract DTOs.

Shape of the shared contract (unchanged from today's nested type):

| Property     | Type                   | Required | Constraints       |
|--------------|------------------------|----------|-------------------|
| `FolderKey`  | `string`               | yes      | `MaxLength(100)`  |
| `FolderType` | `MarketingFolderType`  | yes      | enum value        |

## API / Interface Design
- **Endpoints affected:** the existing Create and Update marketing-action endpoints (whichever controller actions bind `CreateMarketingActionRequest` and `UpdateMarketingActionRequest`). URLs, HTTP methods, request paths, and response shapes are unchanged.
- **OpenAPI schema:** the component currently emitted for the nested type is renamed to `MarketingFolderLinkRequest`. Both `CreateMarketingActionRequest.folderLinks` and `UpdateMarketingActionRequest.folderLinks` reference this component via `$ref`.
- **Generated TypeScript client:** the corresponding interface in the generated client is renamed accordingly. Frontend imports update mechanically.
- **No UI flow changes.**

## Dependencies
- .NET 8 backend project `Anela.Heblo.Application`.
- OpenAPI / NSwag (or equivalent) client generator wired into the build per `docs/development/api-client-generation.md`.
- Existing marketing module handlers, mappers, validators, and tests.
- Frontend marketing-action screens that consume the generated client.

## Out of Scope
- Any change to `MarketingFolderType` (the enum stays as-is).
- Any change to validation rules, field constraints, or business logic for folder links.
- Splitting any other shared-nested-type couplings in other modules. This spec covers only the `CreateFolderLinkRequest` case identified in the brief.
- Database schema changes, migrations, or domain entity changes.
- API versioning or deprecation flow — the wire format is unchanged, so no version bump is needed.
- Renaming any other DTO inside the marketing contracts folder.

## Open Questions
None.

## Status: COMPLETE