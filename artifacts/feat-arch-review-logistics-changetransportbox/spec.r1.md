# Specification: Relocate ChangeTransportBoxState Request/Response into Use Case Subfolder

## Summary
The `ChangeTransportBoxStateRequest` and `ChangeTransportBoxStateResponse` files currently live in the `UseCases/` root of the Logistics module, while every other use case (handler, request, response) is grouped under a per-use-case subfolder. This spec defines a surgical file move plus namespace rename that brings these two files into compliance with the established filesystem convention.

## Background
`docs/architecture/filesystem.md` mandates that complex features place each use case in its own folder containing `Handler`, `Request`, and `Response`. All sibling use cases in `backend/src/Anela.Heblo.Application/Features/Logistics/UseCases/` already follow this pattern (`AddItemToBox/`, `CreateNewTransportBox/`, `GetTransportBoxById/`, etc.). For `ChangeTransportBoxState`, the handler was correctly placed in its own subfolder, but the request and response files were left in the parent `UseCases/` folder with the parent namespace (`Anela.Heblo.Application.Features.Logistics.UseCases` instead of `Anela.Heblo.Application.Features.Logistics.UseCases.ChangeTransportBoxState`).

This inconsistency:
- Makes the Logistics `UseCases/` folder harder to navigate (two stray files at the root).
- Forces the handler to reference a different namespace than every other handler in the module, which is confusing for readers and grep-based searches.
- Drifts from the documented architecture, weakening enforcement for future contributions.

## Functional Requirements

### FR-1: Move request file into the use case subfolder
Relocate `ChangeTransportBoxStateRequest.cs` from `backend/src/Anela.Heblo.Application/Features/Logistics/UseCases/` into `backend/src/Anela.Heblo.Application/Features/Logistics/UseCases/ChangeTransportBoxState/`.

**Acceptance criteria:**
- File exists at `backend/src/Anela.Heblo.Application/Features/Logistics/UseCases/ChangeTransportBoxState/ChangeTransportBoxStateRequest.cs`.
- File no longer exists at the previous location.
- File contents are byte-identical to the original except for the namespace declaration (see FR-3).

### FR-2: Move response file into the use case subfolder
Relocate `ChangeTransportBoxStateResponse.cs` from `backend/src/Anela.Heblo.Application/Features/Logistics/UseCases/` into `backend/src/Anela.Heblo.Application/Features/Logistics/UseCases/ChangeTransportBoxState/`.

**Acceptance criteria:**
- File exists at `backend/src/Anela.Heblo.Application/Features/Logistics/UseCases/ChangeTransportBoxState/ChangeTransportBoxStateResponse.cs`.
- File no longer exists at the previous location.
- File contents are byte-identical to the original except for the namespace declaration (see FR-3).

### FR-3: Update namespace declarations
Change the namespace declaration in both relocated files from `Anela.Heblo.Application.Features.Logistics.UseCases` to `Anela.Heblo.Application.Features.Logistics.UseCases.ChangeTransportBoxState`.

**Acceptance criteria:**
- Both files declare `namespace Anela.Heblo.Application.Features.Logistics.UseCases.ChangeTransportBoxState;` (or block form, matching the existing file style).
- The new namespace matches that of `ChangeTransportBoxStateHandler.cs` in the same folder.

### FR-4: Update all references to the moved types
Every file in the solution (backend project + tests) that previously imported `Anela.Heblo.Application.Features.Logistics.UseCases` solely to reach `ChangeTransportBoxStateRequest` or `ChangeTransportBoxStateResponse` must be updated to import `Anela.Heblo.Application.Features.Logistics.UseCases.ChangeTransportBoxState` instead.

**Acceptance criteria:**
- No file references `Anela.Heblo.Application.Features.Logistics.UseCases.ChangeTransportBoxStateRequest` or `...ChangeTransportBoxStateResponse` under the old namespace.
- The handler in `UseCases/ChangeTransportBoxState/ChangeTransportBoxStateHandler.cs` no longer requires a special `using` for these types (since they now share its namespace), unless other unrelated types still require it.
- Existing `using` directives that were needed only for these two types are removed; directives required for other types remain untouched.
- Controllers, tests, MediatR registrations, and any DI wiring referencing these types compile against the new namespace.

### FR-5: Behavioural preservation
The refactor is purely structural. No logic, contract, serialization, or API surface change is allowed.

**Acceptance criteria:**
- The HTTP endpoint(s) that drive `ChangeTransportBoxState` accept and return identical JSON payloads as before.
- The generated OpenAPI specification is unchanged for any operation backed by these types (DTO names, properties, and order remain identical).
- The generated TypeScript client requires no manual changes.
- All pre-existing tests covering `ChangeTransportBoxState` pass without modification beyond `using` statements.

## Non-Functional Requirements

### NFR-1: Performance
No performance impact expected or permitted. Runtime behaviour, allocations, and request handling cost must be identical.

### NFR-2: Security
No security surface change. Authentication, authorization attributes, and validation rules on the request/response are untouched.

### NFR-3: Maintainability
The resulting `UseCases/` folder layout must match the established pattern across the Logistics module (and the wider project), reducing cognitive load for navigation and grep-based discovery.

### NFR-4: Build & tooling
- Solution builds cleanly: `dotnet build` must succeed with no new warnings.
- `dotnet format` must report no changes after the refactor (formatting preserved).
- OpenAPI client regeneration must produce an identical TypeScript client; if regeneration runs in CI, it must not produce diffs.

## Data Model
No data model changes. The two DTO classes (`ChangeTransportBoxStateRequest`, `ChangeTransportBoxStateResponse`) retain their existing properties, types, and serialization behaviour. Per project rule, these remain plain classes (not C# records) because they are exposed via the OpenAPI client generator.

## API / Interface Design
No external API change. HTTP routes, verbs, request/response schemas, status codes, and error envelopes are preserved exactly. The only internal interface change is the C# namespace of the two DTO classes.

## Dependencies
- The existing `ChangeTransportBoxStateHandler` (already in `UseCases/ChangeTransportBoxState/`).
- The MVC controller(s) and MediatR registration that wire this use case to its HTTP endpoint.
- Any backend unit/integration tests targeting this use case.
- The OpenAPI generation pipeline (must produce an unchanged spec).

## Out of Scope
- Any change to the use case's logic, validation, or state-transition rules.
- Renaming the request/response classes or properties.
- Auditing other modules for similar inconsistencies — this spec covers only `ChangeTransportBoxState` in the Logistics module.
- Adding new tests; existing tests are sufficient since behaviour does not change.
- Converting the DTOs between class and record forms.
- Frontend or generated TypeScript client edits (regeneration alone, if any, must produce no diff).

## Open Questions
None.

## Status: COMPLETE