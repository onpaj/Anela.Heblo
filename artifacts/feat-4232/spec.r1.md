# Specification: Move DownloadFromUrl DTOs into FileStorage Contracts/ folder

## Summary
The FileStorage module's cross-module-callable MediatR types, `DownloadFromUrlRequest` and `DownloadFromUrlResponse`, currently live in the module's internal `UseCases/DownloadFromUrl/` namespace instead of a `Contracts/` folder, contrary to project convention. This is a namespace/folder-move refactor with no behavioral change: relocate the two DTOs to a new `Application/Features/FileStorage/Contracts/` folder and update every reference (handler, validator, DI registration, controller, the cross-module caller in Catalog, and tests) to the new namespace.

## Background
`docs/architecture/development_guidelines.md` mandates: "DTO objects for API (`Request`, `Response`) live in `contracts/` of the specific module" and "Communication between modules exclusively through `contracts/`". The FileStorage module has no `Contracts/` folder today; its `DownloadFromUrlRequest`/`DownloadFromUrlResponse` DTOs sit under `UseCases/DownloadFromUrl/`, alongside the handler — i.e. inside the module's internal implementation namespace.

`ProductExportDownloadJob` in the Catalog module sends `DownloadFromUrlRequest` via MediatR and depends directly on this internal namespace (`Anela.Heblo.Application.Features.FileStorage.UseCases.DownloadFromUrl`). This blurs FileStorage's public surface: nothing distinguishes the two DTOs as a stable, cross-module-callable contract from the handler and other purely internal types that happen to share the same folder. Any future internal restructuring of `UseCases/DownloadFromUrl/` risks silently breaking the Catalog caller, with no folder boundary signaling that risk.

This finding was filed by the daily arch-review routine (2026-09-20) against `docs/architecture/development_guidelines.md`.

## Functional Requirements

### FR-1: Create FileStorage `Contracts/` folder and relocate the DTOs
Create `backend/src/Anela.Heblo.Application/Features/FileStorage/Contracts/` and move the two DTO files into it, updating their namespace from `Anela.Heblo.Application.Features.FileStorage.UseCases.DownloadFromUrl` to `Anela.Heblo.Application.Features.FileStorage.Contracts`:
- `DownloadFromUrlRequest.cs` (currently `UseCases/DownloadFromUrl/DownloadFromUrlRequest.cs`)
- `DownloadFromUrlResponse.cs` (currently `UseCases/DownloadFromUrl/DownloadFromUrlResponse.cs`)

No property, attribute, base-type, or field of either class changes. `DownloadFromUrlRequest` keeps implementing `IRequest<DownloadFromUrlResponse>`; `DownloadFromUrlResponse` keeps deriving from `BaseResponse`.

**Acceptance criteria:**
- `Application/Features/FileStorage/Contracts/DownloadFromUrlRequest.cs` and `.../DownloadFromUrlResponse.cs` exist with namespace `Anela.Heblo.Application.Features.FileStorage.Contracts`.
- The old files no longer exist at `UseCases/DownloadFromUrl/DownloadFromUrlRequest.cs` / `DownloadFromUrlResponse.cs`.
- `UseCases/DownloadFromUrl/DownloadFromUrlHandler.cs` remains in its current location and namespace (it is an internal implementation detail, not a contract); only its `using`/type references change to point at the new namespace.
- No property, method signature, validation attribute, or serialization behavior of either DTO changed — diff is limited to file location and namespace declaration.

### FR-2: Update all in-module references to the new namespace
Update every file within the FileStorage module (Application layer) that references `DownloadFromUrlRequest` or `DownloadFromUrlResponse` to use the new `Contracts` namespace:
- `Application/Features/FileStorage/UseCases/DownloadFromUrl/DownloadFromUrlHandler.cs`
- `Application/Features/FileStorage/Validators/DownloadFromUrlRequestValidator.cs`
- `Application/Features/FileStorage/FileStorageModule.cs` (DI/pipeline registration, if it references the concrete types)

**Acceptance criteria:**
- Each file compiles referencing `Anela.Heblo.Application.Features.FileStorage.Contracts` instead of `...UseCases.DownloadFromUrl`.
- No functional/behavioral change in any of these files — only `using` directives and, where applicable, fully-qualified type references.

### FR-3: Update the cross-module caller in Catalog
Update `Application/Features/Catalog/Infrastructure/Jobs/ProductExportDownloadJob.cs` to reference `DownloadFromUrlRequest`/`DownloadFromUrlResponse` from the new `Anela.Heblo.Application.Features.FileStorage.Contracts` namespace instead of `...UseCases.DownloadFromUrl`. No other change to this file (the `_mediator.Send(...)` call site, field mappings, and surrounding logic are untouched).

**Acceptance criteria:**
- `ProductExportDownloadJob.cs` compiles against the new namespace.
- The MediatR call site and all field assignments (`FileUrl`, `ContainerName`, `BlobName`) are byte-for-byte unchanged aside from the `using` statement.

### FR-4: Update the API controller reference
Update `backend/src/Anela.Heblo.API/Controllers/FileStorageController.cs`, which currently references these types, to use the new `Contracts` namespace.

**Acceptance criteria:**
- `FileStorageController.cs` compiles against the new namespace with no change to endpoint routes, request/response shapes, or controller logic.
- The generated OpenAPI/TypeScript client (`frontend/src/api/generated/api-client.ts`) is regenerated as part of the normal build and shows no shape change to the `DownloadFromUrl` request/response (only a possible internal schema/type-name artifact, if any, is acceptable — the public HTTP contract is unchanged).

### FR-5: Update existing tests to compile against the new namespace
Update `using` directives (and any fully-qualified references) in the following existing test files so the suite compiles and passes unchanged against the new namespace:
- `backend/test/Anela.Heblo.Tests/Features/FileStorage/DownloadFromUrlHandlerTests.cs`
- `backend/test/Anela.Heblo.Tests/Features/FileStorage/FileStorageControllerTests.cs`
- `backend/test/Anela.Heblo.Tests/Features/FileStorage/Pipeline/FileStorageValidationPipelineTests.cs`
- `backend/test/Anela.Heblo.Tests/Features/FileStorage/Validators/DownloadFromUrlRequestValidatorTests.cs`
- `backend/test/Anela.Heblo.Tests/Features/Catalog/Infrastructure/Jobs/ProductExportDownloadJobTests.cs`

**Acceptance criteria:**
- All five test files compile against `Anela.Heblo.Application.Features.FileStorage.Contracts`.
- No test assertion, test data, or test behavior changes — only `using`/namespace references.
- All previously-passing tests in these files still pass after the move.

## Non-Functional Requirements

### NFR-1: Performance
Not applicable. This is a compile-time namespace/folder relocation; no runtime code paths, algorithms, or I/O behavior change. No performance impact is expected or in scope to measure.

### NFR-2: Security
Not applicable. No change to authentication, authorization, validation logic, or data handling. `DownloadFromUrlRequestValidator`'s rules are unchanged; only its `using` statement changes.

### NFR-3: Backward compatibility
This module is currently consumed only in-process via MediatR (no public HTTP contract for the two DTOs directly beyond what `FileStorageController.cs` exposes, which is unaffected in shape). Because the DTOs are C# types referenced at compile time (not reflection-based or string-referenced), the rename is a source-breaking but binary/deploy-safe change: it requires updating all in-repo callers in the same change set (this spec covers all known callers per the FR-1 through FR-5 grep results) but has no external/runtime compatibility concern.

## Data Model
No data model changes. `DownloadFromUrlRequest` and `DownloadFromUrlResponse` keep their existing shape:

**DownloadFromUrlRequest** (implements `IRequest<DownloadFromUrlResponse>`)
- `FileUrl: string` (required)
- `ContainerName: string` (required)
- `BlobName: string?`

**DownloadFromUrlResponse** (extends `BaseResponse`)
- `BlobUrl: string`
- `BlobName: string`
- `ContainerName: string`
- `FileSizeBytes: long`
- (inherited from `BaseResponse`: `Success`, `ErrorCode`, `Params`, etc.)

Only the containing namespace/folder changes, from `Anela.Heblo.Application.Features.FileStorage.UseCases.DownloadFromUrl` to `Anela.Heblo.Application.Features.FileStorage.Contracts`.

## API / Interface Design
No public HTTP API, route, or JSON payload shape changes. This is purely an internal C# namespace reorganization:

- **Before:** `Anela.Heblo.Application.Features.FileStorage.UseCases.DownloadFromUrl.{DownloadFromUrlRequest, DownloadFromUrlResponse}`
- **After:** `Anela.Heblo.Application.Features.FileStorage.Contracts.{DownloadFromUrlRequest, DownloadFromUrlResponse}`

The MediatR request/handler wiring (`IRequestHandler<DownloadFromUrlRequest, DownloadFromUrlResponse>`) is unaffected — MediatR resolves handlers by type, not by namespace/folder, so no DI registration change is required beyond fixing `using` statements where the concrete type is referenced directly.

## Dependencies
- No new external libraries or services.
- Depends on the existing `docs/architecture/development_guidelines.md` `Contracts/` convention already followed by other modules (used here as the precedent/pattern to match).
- Touches the auto-generated OpenAPI TypeScript client (`frontend/src/api/generated/api-client.ts`), which is regenerated as part of the normal build per `docs/development/api-client-generation.md` — no manual frontend changes are anticipated, but the generated file should be regenerated and diffed as part of validation.

## Out of Scope
- Any change to `DownloadFromUrlHandler.cs`'s location, logic, or internal implementation (it stays in `UseCases/DownloadFromUrl/`, it is not a contract).
- Any change to `FileStorageOptions.cs` or `FileDownloadOptions.cs` — the brief notes these also sit flat in the module, but the suggested fix and this spec scope only the `DownloadFromUrl` request/response DTOs. Broader FileStorage module reorganization is a separate concern.
- Any change to `DownloadResilienceService.cs` / `IDownloadResilienceService.cs` (`Infrastructure/` folder) — out of scope.
- Any change to the validator's rules, the handler's retry/resilience/logging behavior, or the controller's routes/behavior.
- Any change to the public HTTP request/response JSON shape or the generated TypeScript client's semantics.
- Introducing a re-export/type-alias shim to preserve the old namespace for compatibility — not needed, since this is a single-repo, compile-time-only change with all callers known and covered above (per FR-1–FR-5).

## Open Questions
None.

## Status: COMPLETE
