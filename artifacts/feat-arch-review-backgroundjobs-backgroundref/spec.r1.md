# Specification: Relocate BackgroundRefresh DTOs to Application Layer

## Summary
Move three DTOs (`RefreshTaskDto`, `RefreshTaskStatusDto`, `RefreshTaskExecutionLogDto`) currently defined in the API project's `Controllers/` folder into the BackgroundJobs module's contracts folder in the Application layer. This restores compliance with the architectural rule that the API project is a thin composition boundary and never owns DTOs.

## Background
The architecture guideline in `docs/architecture/development_guidelines.md` states: *"API project never defines or owns DTOs – it only uses them."* The BackgroundRefresh feature currently violates this rule: three DTOs consumed exclusively by `BackgroundRefreshController` live in `backend/src/Anela.Heblo.API/Controllers/` under the `Anela.Heblo.API.Controllers` namespace.

This placement makes the DTOs invisible to the Application layer, blocks reuse by handlers/tests/other consumers, couples contract types to the API hosting project, and is the exact anti-pattern the guideline exists to prevent. It is also inconsistent with the rest of the BackgroundJobs module, whose other contracts already live under `Anela.Heblo.Application/Features/BackgroundJobs/Contracts/`.

This is a pure architectural cleanup — no behavior, wire format, or generated OpenAPI client shape should change.

## Functional Requirements

### FR-1: Relocate DTO source files
Move the following files from their current location to the BackgroundJobs module contracts folder:

| From | To |
|---|---|
| `backend/src/Anela.Heblo.API/Controllers/RefreshTaskDto.cs` | `backend/src/Anela.Heblo.Application/Features/BackgroundJobs/Contracts/RefreshTaskDto.cs` |
| `backend/src/Anela.Heblo.API/Controllers/RefreshTaskStatusDto.cs` | `backend/src/Anela.Heblo.Application/Features/BackgroundJobs/Contracts/RefreshTaskStatusDto.cs` |
| `backend/src/Anela.Heblo.API/Controllers/RefreshTaskExecutionLogDto.cs` | `backend/src/Anela.Heblo.Application/Features/BackgroundJobs/Contracts/RefreshTaskExecutionLogDto.cs` |

Use `git mv` (or equivalent) so history is preserved.

**Acceptance criteria:**
- The three files no longer exist under `backend/src/Anela.Heblo.API/Controllers/`.
- The three files exist at the target paths above.
- File-level git history is preserved (verifiable via `git log --follow`).

### FR-2: Update namespaces
Update the namespace declaration in each moved file from `Anela.Heblo.API.Controllers` to `Anela.Heblo.Application.Features.BackgroundJobs.Contracts`.

**Acceptance criteria:**
- All three moved files declare namespace `Anela.Heblo.Application.Features.BackgroundJobs.Contracts`.
- No file under `backend/` still references the symbols via the old `Anela.Heblo.API.Controllers` namespace.

### FR-3: Update consumers
Update every `using` directive and fully-qualified reference that currently resolves these three DTOs from `Anela.Heblo.API.Controllers` to `Anela.Heblo.Application.Features.BackgroundJobs.Contracts`. At minimum this includes `BackgroundRefreshController`; any other consumer surfaced by a repo-wide search must also be updated.

**Acceptance criteria:**
- `BackgroundRefreshController.cs` imports `Anela.Heblo.Application.Features.BackgroundJobs.Contracts` and compiles.
- A repo-wide search for `RefreshTaskDto`, `RefreshTaskStatusDto`, and `RefreshTaskExecutionLogDto` shows zero references to the old namespace in source, tests, or generated configuration files (excluding generated TS client output, which will be regenerated).

### FR-4: Preserve DTO contract shape
DTO types remain classes (per project rule: DTOs are classes, never C# records), with identical property names, types, nullability, attributes, and ordering. No fields are added, removed, renamed, or retyped.

**Acceptance criteria:**
- A diff of each moved file vs. its prior version shows only the namespace change (and, if required by style, removal of an unused `using`).
- The OpenAPI document produced by the API (`swagger.json` for the BackgroundRefresh endpoints) is byte-equivalent at the schema component level for these three types after the move, aside from any `$ref` path changes the generator derives from CLR namespaces. Generated client shape (TypeScript and any C# clients) must remain functionally equivalent.

### FR-5: Regenerate API clients if needed
If the OpenAPI generator embeds CLR namespace into schema names or generated client class names, regenerate the TypeScript client (`frontend/`) per `docs/development/api-client-generation.md` and commit the regenerated output alongside the BE change.

**Acceptance criteria:**
- `npm run build` in `frontend/` succeeds.
- If the generated TS client changed, the regenerated files are committed.
- If no regenerated client output changed, no frontend files are modified.

## Non-Functional Requirements

### NFR-1: Build & format
- `dotnet build` of the solution succeeds with zero new warnings.
- `dotnet format` reports no changes required.

### NFR-2: Tests
- All existing unit and integration tests under `backend/test/` pass without modification beyond namespace updates in `using` statements.
- No new tests are required (this is a relocation with no behavioral change).

### NFR-3: Backward compatibility
- HTTP wire contract for every endpoint on `BackgroundRefreshController` is unchanged (route, verb, request body schema, response body schema, status codes).
- No database, configuration, or environment changes.

### NFR-4: Architectural compliance
- The API project (`Anela.Heblo.API`) defines zero DTOs after the change. A reviewer scanning `backend/src/Anela.Heblo.API/` should find no `*Dto.cs` files outside of acceptable composition concerns (e.g., none).

## Data Model
No data model changes. The three DTOs retain their existing fields:
- `RefreshTaskDto` — descriptor of a refresh task
- `RefreshTaskStatusDto` — runtime status snapshot for a refresh task
- `RefreshTaskExecutionLogDto` — log entry for a single execution

The exact field set is preserved verbatim from the existing files.

## API / Interface Design
No API surface changes. Endpoints exposed by `BackgroundRefreshController` keep their routes, verbs, request/response shapes, and authentication requirements. Only the CLR namespace of the DTO types changes; the JSON contract is unchanged.

## Dependencies
- **Project reference:** `Anela.Heblo.API` already references `Anela.Heblo.Application` (the standard layering). No new project references are required. Verify this reference exists; if it doesn't, the move would surface a missing dependency and that must be addressed as part of the change.
- **OpenAPI generation pipeline** (`docs/development/api-client-generation.md`) — only invoked if generated client output is affected.

## Out of Scope
- Refactoring `BackgroundRefreshController` itself (logic, routes, error handling).
- Migrating `BackgroundRefreshController` from MVC to MediatR handlers.
- Auditing other controllers in the API project for the same anti-pattern (each such finding gets its own brief).
- Adding new fields or new DTOs.
- Changing DTO conventions (e.g., switching to records, adding validation attributes).
- Modifying tests beyond mechanical `using`-directive updates.

## Open Questions
None.

## Status: COMPLETE