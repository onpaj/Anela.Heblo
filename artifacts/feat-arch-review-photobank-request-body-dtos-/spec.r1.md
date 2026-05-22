# Specification: Relocate Photobank request-body DTOs to the Application project

## Summary
Four request-body DTO classes (`AddPhotoTagBody`, `CreateTagBody`, `BulkAddPhotoTagBody`, `BulkAddPhotoTagByIdsBody`) are currently declared at the bottom of `PhotobankController.cs` inside the `Anela.Heblo.API` project. This violates the documented rule that the API project never owns DTOs. Move all four into `Anela.Heblo.Application/Features/Photobank/Contracts/` with no behavioral changes, restoring the architectural boundary while keeping the generated API clients identical.

## Background
The project follows Clean Architecture with a Vertical Slice organization. Per `docs/architecture/development_guidelines.md`, the rule is explicit: *"API project never defines or owns DTOs — it only uses them."* All request/response DTOs must live in `Application/Features/<Module>/Contracts/`.

The Photobank module already honors this convention for its other contracts — `IndexRootDto`, `PhotoDto`, `TagDto`/`TagWithCountDto`, and `TagRuleDto` all live under `Anela.Heblo.Application/Features/Photobank/Contracts/`. The four request-body classes are the lone exception: they were declared inline in the controller file.

Because NSwag generates the TypeScript client from the API surface, these types are currently emitted into `frontend/src/api/generated/api-client.ts` (e.g. `CreateTagBody`). Moving the classes to the Application project does not change their public shape or namespace-independent serialization, so the generated client output remains byte-for-byte identical — but ownership becomes correct and the contracts become reusable (e.g. by FluentValidation validators) without inverting the project dependency direction.

This is a pure structural refactor surfaced by the daily arch-review routine on 2026-05-21. No new functionality is introduced.

## Functional Requirements

### FR-1: Move the four request-body DTOs into the Contracts folder
Each of the four classes is moved verbatim (same name, same properties, same default initializers, same nullability) from `PhotobankController.cs` into its own file under `backend/src/Anela.Heblo.Application/Features/Photobank/Contracts/`:

| Class | Properties (unchanged) |
|-------|------------------------|
| `AddPhotoTagBody` | `string TagName { get; set; } = null!;` |
| `CreateTagBody` | `string Name { get; set; } = string.Empty;` |
| `BulkAddPhotoTagBody` | `List<string>? Tags`, `string? Search`, `string TagName = null!` |
| `BulkAddPhotoTagByIdsBody` | `List<int> PhotoIds = []`, `string TagName = null!` |

Each class is placed in the `Anela.Heblo.Application.Features.Photobank.Contracts` namespace, matching the existing files in that folder.

**Acceptance criteria:**
- Four new files exist: `Contracts/AddPhotoTagBody.cs`, `Contracts/CreateTagBody.cs`, `Contracts/BulkAddPhotoTagBody.cs`, `Contracts/BulkAddPhotoTagByIdsBody.cs`.
- Each file declares exactly one of the four classes in namespace `Anela.Heblo.Application.Features.Photobank.Contracts`.
- Property names, types, accessors, and default initializers are identical to the originals at `PhotobankController.cs:425–446`.
- The classes are declared as `class` (not `record`), per the project rule that DTOs are classes for OpenAPI generator compatibility.
- `List<>` usages compile (file uses a `using System.Collections.Generic;` directive or the project enables `ImplicitUsings`).

### FR-2: Remove the inline class declarations from the controller file
The four class definitions at `PhotobankController.cs:425–446` are deleted. The controller adds `using Anela.Heblo.Application.Features.Photobank.Contracts;` so the action signatures (`CreateTag`, `AddPhotoTag`, `BulkAddPhotoTag`, `BulkAddPhotoTagByIds`) continue to resolve the body types.

**Acceptance criteria:**
- `PhotobankController.cs` no longer contains any of the four `public class …Body` declarations.
- The controller imports the `Contracts` namespace.
- All four endpoint signatures still bind their `[FromBody]` parameters to the relocated types with no signature change.
- No controller logic, attributes, routes, or status-code declarations are modified.

### FR-3: Preserve API and generated-client compatibility
The HTTP contract (route, verb, request body shape, response shape) is unchanged. The regenerated TypeScript client must continue to expose the same type names with the same members.

**Acceptance criteria:**
- The four request bodies serialize/deserialize identically (same JSON property names, same optional/required semantics).
- After client regeneration, `frontend/src/api/generated/api-client.ts` contains the same `CreateTagBody`, `AddPhotoTagBody`, `BulkAddPhotoTagBody`, and `BulkAddPhotoTagByIdsBody` definitions as before the change (no consumer code edits required).

## Non-Functional Requirements

### NFR-1: Performance
No runtime performance impact. This is a compile-time relocation of type declarations with no change to request handling.

### NFR-2: Security
No security impact. Authorization attributes, roles (`SuperUser`, `MarketingWriter`), and validation behavior are untouched. No secrets, auth, or input-validation logic is involved.

### NFR-3: Maintainability
The change restores conformance to the documented DTO ownership boundary, removing a precedent that would otherwise make the boundary harder to enforce for future contributors. File organization follows the one-type-per-file convention used by the surrounding Contracts files.

### NFR-4: Backward compatibility
Zero breaking changes to the public HTTP API or to frontend consumers. The refactor is internal to the backend project layout.

## Data Model
No data-model changes. The relocated classes are transport DTOs only; they are mapped onto the existing MediatR request types (`CreateTagRequest`, `AddPhotoTagRequest`, `BulkAddPhotoTagRequest`, `BulkAddPhotoTagByIdsRequest`) by the controller exactly as today. No entities, persistence, or migrations are affected.

## API / Interface Design
No interface design changes. Affected endpoints (unchanged):

- `POST /api/photobank/tags` — body `CreateTagBody`
- `POST /api/photobank/photos/{id}/tags` — body `AddPhotoTagBody`
- `POST /api/photobank/photos/bulk-tag` — body `BulkAddPhotoTagBody`
- `POST /api/photobank/photos/tag-by-ids` — body `BulkAddPhotoTagByIdsBody`

## Dependencies
- `Anela.Heblo.API` already references `Anela.Heblo.Application` (it imports the Photobank UseCases and Contracts namespaces), so the relocated types are reachable from the controller with no new project reference.
- NSwag / OpenAPI TypeScript client generation runs on build; the client must be regenerated as part of validation to confirm FR-3.

## Out of Scope
- Adding FluentValidation validators or any input-validation logic to the relocated DTOs.
- Renaming the classes (e.g. dropping the `Body` suffix) or changing their property names/shapes.
- Converting the DTOs to records (explicitly prohibited by the project rule).
- Moving or refactoring any other DTOs, request types, or controller logic.
- Any change to the `RetagPhotos`, `AddRoot`, `AddRule`, or `UpdateRule` endpoints, which bind MediatR request types directly rather than dedicated body classes.

## Open Questions
None.

## Status: COMPLETE
