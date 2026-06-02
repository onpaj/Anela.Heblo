# Specification: Relocate Picking List Operation DTOs from Domain to Application Layer

## Summary
Move `PrintPickingListRequest`, `PrintPickingListResult`, and `IPickingListSource` from the `Anela.Heblo.Domain` project to the `Anela.Heblo.Application` project to restore Clean Architecture dependency rules. These three types are operation-level (use-case) contracts and a source port that depends on them, not domain concepts, and their current location forces the most stable layer to depend on application/infrastructure concerns.

## Background
The Logistics module's picking subsystem currently exposes three types from the Domain project that conceptually belong to the Application layer:

- `backend/src/Anela.Heblo.Domain/Features/Logistics/Picking/PrintPickingListRequest.cs` (lines 1–26) — carries `SendToPrinter`, `ChangeOrderState`, and `DefaultCarriers`, all of which are application-level workflow/configuration switches, not domain invariants.
- `backend/src/Anela.Heblo.Domain/Features/Logistics/Picking/PrintPickingListResult.cs` (lines 1–10) — returns `ExportedFiles` (file paths) and `OrderIds`, which describe the output of a use case rather than a domain value.
- `backend/src/Anela.Heblo.Domain/Features/Logistics/Picking/IPickingListSource.cs` (lines 1–9) — a port whose signature depends on the two DTOs above, so it also anchors application behaviour inside Domain.

Clean Architecture (followed across this repository, per `docs/architecture/development_guidelines.md` and `docs/📘 Architecture Documentation – MVP Work.md`) requires Domain to contain only entities, value objects, aggregate roots, and pure domain service ports. Application-operation request/response shapes belong in the Application layer. Leaving these types in Domain inverts the dependency rule: any future change to how printing is dispatched (file vs. queue), or to workflow flags, would force edits in the most stable layer.

This finding was filed by the daily arch-review routine on 2026-05-28.

## Functional Requirements

### FR-1: Relocate `PrintPickingListRequest` to the Application layer
Move the `PrintPickingListRequest` type out of the Domain project and into the Application project, under the existing Logistics feature folder. The type's public surface (properties, default values, accessibility, mutability) must remain identical so consumers do not need behavioural changes.

**Target location:** `backend/src/Anela.Heblo.Application/Features/Logistics/Picking/PrintPickingListRequest.cs`
**Target namespace:** `Anela.Heblo.Application.Features.Logistics.Picking`

**Acceptance criteria:**
- The file no longer exists under `backend/src/Anela.Heblo.Domain/Features/Logistics/Picking/`.
- The file exists at the target location with the target namespace.
- All public properties (`SendToPrinter`, `ChangeOrderState`, `DefaultCarriers`, and any others currently declared) are preserved verbatim, including types, nullability, defaults, and accessors.
- `dotnet build` of the full solution succeeds.

### FR-2: Relocate `PrintPickingListResult` to the Application layer
Move `PrintPickingListResult` from Domain to Application alongside `PrintPickingListRequest`. As with FR-1, the type's public shape must be preserved.

**Target location:** `backend/src/Anela.Heblo.Application/Features/Logistics/Picking/PrintPickingListResult.cs`
**Target namespace:** `Anela.Heblo.Application.Features.Logistics.Picking`

**Acceptance criteria:**
- The file no longer exists under `backend/src/Anela.Heblo.Domain/Features/Logistics/Picking/`.
- The file exists at the target location with the target namespace.
- All public properties (`ExportedFiles`, `OrderIds`, and any others currently declared) are preserved verbatim.
- `dotnet build` of the full solution succeeds.

### FR-3: Relocate `IPickingListSource` to the Application layer
Move the `IPickingListSource` interface from Domain to Application, sharing the new folder with FR-1 and FR-2. The interface signature (method names, parameter types, return types) must remain unchanged.

**Target location:** `backend/src/Anela.Heblo.Application/Features/Logistics/Picking/IPickingListSource.cs`
**Target namespace:** `Anela.Heblo.Application.Features.Logistics.Picking`

**Acceptance criteria:**
- The file no longer exists under `backend/src/Anela.Heblo.Domain/Features/Logistics/Picking/`.
- The file exists at the target location with the target namespace.
- The interface signature is unchanged.
- `dotnet build` of the full solution succeeds.

### FR-4: Update all consumers' namespace imports
Identify every file in the solution that references any of the three moved types via their old Domain namespace and update the `using` directives (or fully qualified names) to point to the new Application namespace. The brief notes "the single namespace reference in any handler that currently imports them from the Domain path" — verify whether it is truly singular and update **all** references found.

**Search scope:** the entire `backend/` tree, including any infrastructure project that implements `IPickingListSource`.

**Acceptance criteria:**
- A solution-wide search for `Anela.Heblo.Domain.Features.Logistics.Picking` returns no matches referencing the three moved types.
- All files that previously imported the types from the old namespace now import them from `Anela.Heblo.Application.Features.Logistics.Picking`.
- `dotnet build` succeeds with zero errors and no new warnings introduced by the move.

### FR-5: Verify and (if necessary) clean up the empty Domain folder
After the three files are moved, the folder `backend/src/Anela.Heblo.Domain/Features/Logistics/Picking/` may be empty. If it contains no other files, remove the empty folder so it does not give the false impression that Domain still owns picking concerns. If other files remain, leave the folder in place and leave those files untouched.

**Acceptance criteria:**
- If the Domain `Picking` folder is empty after the move, it is removed.
- If it is not empty, it is left as-is and the remaining files are not modified.

### FR-6: Preserve behavioural and test parity
This is a pure relocation — no behaviour, validation, defaults, or wire shapes should change. Existing tests that reference these types should pass without modification beyond updating their `using` directives.

**Acceptance criteria:**
- All unit and integration tests that touch the picking subsystem pass after the move.
- `dotnet test` for the solution returns the same pass count as before the change, except for any test files whose `using` directives were updated (which must also pass).
- No tests are added, removed, skipped, or modified beyond `using` directive updates.

## Non-Functional Requirements

### NFR-1: Performance
No runtime performance impact. This is a compile-time relocation of type declarations; no IL hot path changes.

### NFR-2: Security
No security impact. No authentication, authorization, input handling, or secret handling is touched.

### NFR-3: Maintainability / Architecture Compliance
After the change, the Domain project must not contain operation-level DTOs or application-port interfaces for the Picking subsystem. The change must conform to the dependency rule documented in `docs/architecture/development_guidelines.md` and `docs/📘 Architecture Documentation – MVP Work.md`: Application depends on Domain; Domain depends on neither Application nor Infrastructure.

**Verification:** the Domain project's references must not be expanded to satisfy the build (i.e. Domain still does not reference Application). Existing Domain → Application or Domain → Infrastructure dependencies, if any unrelated to this change, are not introduced by this work.

### NFR-4: Backwards Compatibility
There are no external consumers of these CLR types outside this repository (they are not part of any published NuGet package or OpenAPI contract — the OpenAPI client generation operates on HTTP endpoints, not internal namespaces). No deprecation shims, type forwarders, or compatibility wrappers are required.

### NFR-5: Code Style
The moved files must follow the project's existing C# formatting. Run `dotnet format` after the move and commit any whitespace/style changes that result. Do not perform any other stylistic refactoring of the moved code (per the repository's "surgical changes" rule).

## Data Model
No data model changes. No entities, value objects, database tables, migrations, or persistence mappings are affected. The types being moved are in-memory operation DTOs and a port interface; they have no persistence representation.

## API / Interface Design
No HTTP API, MediatR contract, or UI flow changes. The change is internal to the .NET project structure:

- **Before:** `Anela.Heblo.Domain.Features.Logistics.Picking.{PrintPickingListRequest, PrintPickingListResult, IPickingListSource}`
- **After:** `Anela.Heblo.Application.Features.Logistics.Picking.{PrintPickingListRequest, PrintPickingListResult, IPickingListSource}`

The OpenAPI surface, the generated TypeScript client, and the frontend are unaffected. No regeneration of clients is required.

## Dependencies
- The `Anela.Heblo.Application` project must already reference (transitively or directly) any types used by the three moved files. If `PrintPickingListRequest`, `PrintPickingListResult`, or `IPickingListSource` reference Domain types in their signatures (e.g. order or carrier domain types), those references continue to resolve correctly because Application already depends on Domain.
- No new NuGet packages.
- No changes to the DI container registration are expected unless an implementation of `IPickingListSource` lives in a project that currently references Domain but not Application. If that situation exists, the implementing project must already (or must now) reference Application; given that handlers in this codebase already live in Application, this is highly likely to be true without further changes. Verify during implementation.

## Out of Scope
- Renaming any of the three types.
- Changing any property or method signatures.
- Refactoring `PrintPickingListRequest` to remove `SendToPrinter`, `ChangeOrderState`, or `DefaultCarriers`, or otherwise redesigning these as multiple finer-grained types. (Such redesign is a legitimate follow-up but is not part of this relocation.)
- Introducing a new abstraction layer (e.g. a separate `Contracts` project).
- Touching any other types in `backend/src/Anela.Heblo.Domain/Features/Logistics/` outside the `Picking/` folder.
- Touching the frontend or regenerating the OpenAPI client.
- Reviewing or relocating other Domain-layer types in other modules that may have similar smells — this brief scopes the work to the Picking trio only.
- Adding new tests; only updating `using` directives of existing tests is in scope.

## Open Questions
None.

## Status: COMPLETE