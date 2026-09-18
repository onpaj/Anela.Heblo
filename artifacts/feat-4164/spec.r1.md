# Specification: PackingMaterials — co-locate misplaced MediatR Request/Response types with their handlers

## Summary
Four MediatR request/response type pairs for the `PackingMaterials` feature currently live in `Application/Features/PackingMaterials/Contracts/` even though each belongs to exactly one use case whose handler already lives in its own `UseCases/{UseCaseName}/` folder. This violates the co-location convention documented in `docs/architecture/filesystem.md` (`Contracts/` is reserved for DTOs shared **across** use cases). This is a pure structural refactor — move each request/response class into its owning `UseCases/{UseCaseName}/` folder, update its namespace and every reference to it, and delete the now-empty `Contracts/` file. No business logic, validation, routing, or public API surface changes.

## Background
An automated architecture-review pass (issue #4164) found that `GetPackingMaterialsListRequest`/`Response`, `CreatePackingMaterialRequest`/`Response`, `UpdatePackingMaterialRequest`/`Response`, and `UpdatePackingMaterialQuantityRequest`/`Response` sit in `Contracts/` instead of alongside their handlers in `UseCases/GetPackingMaterialsList/`, `UseCases/CreatePackingMaterial/`, `UseCases/UpdatePackingMaterial/`, and `UseCases/UpdatePackingMaterialQuantity/` respectively. Every sibling use case in this same module (`GetAllocations`, `CreateAllocation`, `UpdateAllocation`, `DeleteAllocation`, `GetConsumptionHistory`, `GetDailyConsumptionBreakdown`, `GetPackingMaterialLogs`, `ProcessDailyConsumption`, `DeletePackingMaterial`) already follows the correct pattern — Request/Response/Handler in one `UseCases/{Name}/` folder — so this brings the four outliers into line with the module's own established convention, not a new one. Verified against the current tree: the four target `UseCases/*` folders each currently contain only a `*Handler.cs` file, confirming the Request/Response are indeed missing from where they belong.

## Functional Requirements

### FR-1: Relocate `GetPackingMaterialsList` request/response
Move the `GetPackingMaterialsListRequest` and `GetPackingMaterialsListResponse` classes (both currently declared in the single file `Contracts/GetPackingMaterialsListRequest.cs`) into `UseCases/GetPackingMaterialsList/`, as two files: `GetPackingMaterialsListRequest.cs` and `GetPackingMaterialsListResponse.cs`. Update their namespace from `Anela.Heblo.Application.Features.PackingMaterials.Contracts` to `Anela.Heblo.Application.Features.PackingMaterials.UseCases.GetPackingMaterialsList`.

**Acceptance criteria:**
- `Contracts/GetPackingMaterialsListRequest.cs` no longer exists.
- `UseCases/GetPackingMaterialsList/GetPackingMaterialsListRequest.cs` and `UseCases/GetPackingMaterialsList/GetPackingMaterialsListResponse.cs` exist, each declaring exactly one class, in namespace `...UseCases.GetPackingMaterialsList`.
- `GetPackingMaterialsListHandler.cs` and any other consumer no longer need a `using ...Contracts;` import for these two types (the redundant `using` is removed if it becomes unused after the move — `PackingMaterialDto` still requires the `Contracts` import, so verify per-file whether the `using` is still needed rather than removing it unconditionally).
- Behavior, route, and JSON shape of the `GET` list endpoint are unchanged.

### FR-2: Relocate `CreatePackingMaterial` request/response
Move `CreatePackingMaterialRequest` and `CreatePackingMaterialResponse` (currently both in `Contracts/CreatePackingMaterialRequest.cs`) into `UseCases/CreatePackingMaterial/`, as two files, namespace `Anela.Heblo.Application.Features.PackingMaterials.UseCases.CreatePackingMaterial`.

**Acceptance criteria:**
- `Contracts/CreatePackingMaterialRequest.cs` no longer exists.
- `UseCases/CreatePackingMaterial/CreatePackingMaterialRequest.cs` and `UseCases/CreatePackingMaterial/CreatePackingMaterialResponse.cs` exist in the new namespace.
- `CreatePackingMaterialHandler.cs`, `PackingMaterialsController.cs`, and any test referencing these types compile against the new namespace.
- Behavior, route, and JSON shape of the create endpoint are unchanged.

### FR-3: Relocate `UpdatePackingMaterial` request/response
Move `UpdatePackingMaterialRequest` and `UpdatePackingMaterialResponse` (currently both in `Contracts/UpdatePackingMaterialRequest.cs`) into `UseCases/UpdatePackingMaterial/`, as two files, namespace `Anela.Heblo.Application.Features.PackingMaterials.UseCases.UpdatePackingMaterial`.

**Acceptance criteria:**
- `Contracts/UpdatePackingMaterialRequest.cs` no longer exists.
- `UseCases/UpdatePackingMaterial/UpdatePackingMaterialRequest.cs` and `UseCases/UpdatePackingMaterial/UpdatePackingMaterialResponse.cs` exist in the new namespace.
- `UpdatePackingMaterialHandler.cs`, `PackingMaterialsController.cs`, and any test referencing these types compile against the new namespace.
- Behavior, route, and JSON shape of the update endpoint are unchanged.

### FR-4: Relocate `UpdatePackingMaterialQuantity` request/response
Move `UpdatePackingMaterialQuantityRequest.cs` and `UpdatePackingMaterialQuantityResponse.cs` (currently two separate files in `Contracts/`) into `UseCases/UpdatePackingMaterialQuantity/`, unchanged in file-per-class shape, namespace `Anela.Heblo.Application.Features.PackingMaterials.UseCases.UpdatePackingMaterialQuantity`.

**Acceptance criteria:**
- `Contracts/UpdatePackingMaterialQuantityRequest.cs` and `Contracts/UpdatePackingMaterialQuantityResponse.cs` no longer exist.
- `UseCases/UpdatePackingMaterialQuantity/UpdatePackingMaterialQuantityRequest.cs` and `.../UpdatePackingMaterialQuantityResponse.cs` exist in the new namespace.
- `UpdatePackingMaterialQuantityHandler.cs`, `PackingMaterialsController.cs`, and any test referencing these types compile against the new namespace.
- Behavior, route, and JSON shape of the update-quantity endpoint are unchanged.

### FR-5: Update every reference to the moved types
Every file that references any of the eight moved classes by `using` directive or fully-qualified name must be updated to reference the new namespace. Known reference sites (grep-verified against the current tree):
- `Application/Features/PackingMaterials/UseCases/GetPackingMaterialsList/GetPackingMaterialsListHandler.cs`
- `Application/Features/PackingMaterials/UseCases/CreatePackingMaterial/CreatePackingMaterialHandler.cs`
- `Application/Features/PackingMaterials/UseCases/UpdatePackingMaterial/UpdatePackingMaterialHandler.cs`
- `Application/Features/PackingMaterials/UseCases/UpdatePackingMaterialQuantity/UpdatePackingMaterialQuantityHandler.cs`
- `API/Controllers/PackingMaterialsController.cs`
- `test/Anela.Heblo.Tests/Features/PackingMaterials/GetPackingMaterialsListHandlerTests.cs`
- `test/Anela.Heblo.Tests/Features/PackingMaterials/PackingMaterialCrudHandlerTests.cs`
- `test/Anela.Heblo.Tests/Features/PackingMaterials/PackingMaterialLogPersistenceTests.cs`
- `test/Anela.Heblo.Tests/Features/PackingMaterials/PackingMaterialsControllerNotFoundTests.cs`
- `test/Anela.Heblo.Tests/Features/PackingMaterials/PackingMaterialsListQueryCountTests.cs`

**Acceptance criteria:**
- `dotnet build` succeeds for the whole solution with zero new warnings/errors introduced by the move.
- No file outside this list is found (by a fresh grep for each of the 8 type names) still importing the old `...PackingMaterials.Contracts` namespace for these specific types after the change.
- `Contracts/PackingMaterialDto.cs`, `Contracts/ConsumptionDetailDto.cs`, `Contracts/IInvoiceConsumptionSource.cs`, and other genuinely shared Contracts types are **not** moved and are left exactly where they are.

## Non-Functional Requirements

### NFR-1: No behavior change
This is a namespace/file-location refactor only. No change to MediatR pipeline registration, controller routes, DTO shapes, validation rules, or persistence. Existing unit tests for these four use cases must pass unmodified in behavior (only `using`/namespace references may need updating to compile).

### NFR-2: Build and format compliance
Must pass `dotnet build` and `dotnet format` (per repo validation rules) with no new violations. `MediatR` DI auto-registration (assembly scanning) must continue to discover the moved handlers/requests without any explicit registration change, since namespace changes do not affect assembly-scan-based registration — this is verified as a build/runtime smoke check, not assumed.

## Data Model
No data model changes. No entities, EF configurations, or migrations are touched — this refactor is confined to the `Anela.Heblo.Application` project's `Features/PackingMaterials` folder (request/response DTO types only) and its consumers.

## API / Interface Design
No public API changes. The four affected endpoints on `PackingMaterialsController` (list, create, update, update-quantity) keep their routes, HTTP verbs, request bodies, and response shapes exactly as-is; only the C# namespace of the underlying MediatR request/response types changes, which is invisible to API consumers (the generated OpenAPI/TypeScript client is unaffected since it's driven by controller action signatures and DTO shapes, not by internal namespaces — this should be confirmed by running the client generation step and diffing output, per NFR-2's build check).

## Dependencies
None beyond what already exists (MediatR, the PackingMaterials module itself). No new packages, no new endpoints, no coordination with other modules.

## Out of Scope
- Any change to `Contracts/` types that are genuinely shared across use cases (`PackingMaterialDto`, `ConsumptionDetailDto`, `ConsumptionGroupBy`, `ConsumptionGroupDto`, `IInvoiceConsumptionSource`, `InvoiceConsumptionHeader`, `MaterialConsumptionHistoryItemDto`, `PackingMaterialAllocationDto`, `PackingMaterialLogDto`, `PackingMaterialsTextHelper`, `CreateAllocationRequestBody`, `UpdateAllocationRequestBody`, `UpdateQuantityRequest`) — these stay in `Contracts/` as-is.
- Any similar arch-review cleanup in other feature modules — this spec is scoped to `PackingMaterials` only, per issue #4164.
- Any behavior, validation, or API contract change.
- Renaming the classes themselves — only their file location and namespace change; class names are unchanged.
- `UpdateQuantityRequest.cs` in `Contracts/` — distinct from `UpdatePackingMaterialQuantityRequest`; not in scope unless investigation during architecture review finds it to be an actual duplicate/dead type (flagged as an open question below, not assumed).

## Open Questions
None. The suggested fix in the issue is unambiguous and this spec follows it directly; the one adjacent observation (`Contracts/UpdateQuantityRequest.cs`, a similarly-named but distinct file) is called out explicitly above as out of scope rather than left as a blocking question.

## Status: COMPLETE
