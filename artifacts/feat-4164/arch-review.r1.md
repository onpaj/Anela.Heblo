# Architecture Review: PackingMaterials — co-locate misplaced MediatR Request/Response types

## Skip Design: true
Pure backend file-move/namespace refactor. No new or changed UI components, screens, layouts, endpoints, routes, request/response JSON shapes, or persistence. No design-phase work is needed; the designer agent should produce a minimal backend-only component-design note (or explicitly state none is needed) and move straight to planning.

## Architectural Fit Assessment
This aligns exactly with the module's own established convention — it does not introduce one. Verified against the current tree: every other use case in `Features/PackingMaterials/UseCases/` (`GetAllocations`, `CreateAllocation`, `UpdateAllocation`, `DeleteAllocation`, `GetConsumptionHistory`, `GetDailyConsumptionBreakdown`, `GetPackingMaterialLogs`, `ProcessDailyConsumption`, `DeletePackingMaterial`) already places its `Request.cs`/`Response.cs` in the same folder as its `Handler.cs`, with a `UseCases.{Name}` namespace (confirmed by reading `GetAllocationsRequest.cs`, namespace `Anela.Heblo.Application.Features.PackingMaterials.UseCases.GetAllocations`). The four outliers named in the issue are the only exceptions in this module. `docs/architecture/filesystem.md` §"Application Layer" states `Features/{Feature}/UseCases/`: "Each use case in separate folder with Handler, Request, Response" and `Features/{Feature}/Contracts/`: "Shared DTOs across multiple use cases" — the four moved types are single-use-case, not shared, so this is a straightforward compliance fix with no architectural ambiguity.

Read the DI registration (`PackingMaterialsModule.cs`) to rule out a hidden coupling risk: it contains the comment "MediatR handlers are automatically registered by MediatR scan" and registers no explicit request-type mapping. MediatR's default `AddMediatR(...)` assembly-scan registration keys handlers by their `IRequestHandler<TRequest, TResponse>` closed generic interface, resolved by reflection over the assembly — it does not depend on namespace, only on the assembly containing the type and the interface implementation. Moving these types to a new namespace within the same assembly (`Anela.Heblo.Application`) is therefore safe and requires no DI registration change. This was verified by inspection, not assumed.

## Proposed Architecture

### Component Overview
No new components. Existing structure, corrected:

```
Features/PackingMaterials/
├── UseCases/
│   ├── GetPackingMaterialsList/
│   │   ├── GetPackingMaterialsListHandler.cs   (existing, unchanged logic)
│   │   ├── GetPackingMaterialsListRequest.cs   (moved from Contracts/)
│   │   └── GetPackingMaterialsListResponse.cs  (moved from Contracts/, split out of the same file)
│   ├── CreatePackingMaterial/
│   │   ├── CreatePackingMaterialHandler.cs     (existing, unchanged logic)
│   │   ├── CreatePackingMaterialRequest.cs     (moved from Contracts/)
│   │   └── CreatePackingMaterialResponse.cs    (moved from Contracts/, split out of the same file)
│   ├── UpdatePackingMaterial/
│   │   ├── UpdatePackingMaterialHandler.cs     (existing, unchanged logic)
│   │   ├── UpdatePackingMaterialRequest.cs     (moved from Contracts/)
│   │   └── UpdatePackingMaterialResponse.cs    (moved from Contracts/, split out of the same file)
│   └── UpdatePackingMaterialQuantity/
│       ├── UpdatePackingMaterialQuantityHandler.cs   (existing, unchanged logic)
│       ├── UpdatePackingMaterialQuantityRequest.cs   (moved from Contracts/, already its own file)
│       └── UpdatePackingMaterialQuantityResponse.cs  (moved from Contracts/, already its own file)
└── Contracts/
    ├── PackingMaterialDto.cs                (stays — shared across GetPackingMaterialsList, CreatePackingMaterial, UpdatePackingMaterial, UpdatePackingMaterialQuantity, GetAllocations, etc.)
    ├── ConsumptionDetailDto.cs, ConsumptionGroupBy.cs, ConsumptionGroupDto.cs   (stay — shared)
    ├── IInvoiceConsumptionSource.cs, InvoiceConsumptionHeader.cs               (stay — shared)
    ├── MaterialConsumptionHistoryItemDto.cs, PackingMaterialAllocationDto.cs   (stay — shared)
    ├── PackingMaterialLogDto.cs, PackingMaterialsTextHelper.cs                 (stay — shared)
    ├── CreateAllocationRequestBody.cs, UpdateAllocationRequestBody.cs, UpdateQuantityRequest.cs (stay — out of scope, distinct types)
```

### Key Design Decisions

#### Decision 1: One class per file, split where two classes currently share a file
**Options considered:**
(a) Keep `GetPackingMaterialsListRequest.cs` containing both `...Request` and `...Response` classes, just move the whole file; (b) split into two files named after each class, matching the pattern already used by every sibling use case (e.g. `GetAllocationsRequest.cs` + `GetAllocationsResponse.cs`).
**Chosen approach:** (b) — split into `{UseCase}Request.cs` and `{UseCase}Response.cs`, one class per file.
**Rationale:** Every existing `UseCases/{Name}/` folder in this module (and per `filesystem.md`'s own tree example, `Get{Entity}List/Get{Entity}ListRequest.cs` + `Get{Entity}ListResponse.cs` as separate files) follows one-class-per-file for Request/Response. Keeping `GetPackingMaterialsListRequest.cs` and `CreatePackingMaterialRequest.cs` as combined files would move the misplacement problem instead of fixing it — the goal is consistency with siblings, not just a change of directory. `UpdatePackingMaterialQuantityRequest.cs`/`Response.cs` already are separate files, so only `GetPackingMaterialsListRequest.cs` and `CreatePackingMaterialRequest.cs` need to be split (2 of the 4 outliers).

#### Decision 2: Namespace becomes `UseCases.{UseCaseName}`, matching every sibling use case
**Options considered:** (a) Move the files but keep the `...Contracts` namespace to minimize consumer edits; (b) change the namespace to `...UseCases.{UseCaseName}`, matching `GetAllocationsRequest`, `DeletePackingMaterialRequest`, etc.
**Chosen approach:** (b).
**Rationale:** (a) is a half-measure — it moves the file physically but the type would still logically read as belonging to `Contracts` from any consumer's `using` list, defeating the stated purpose of the issue (developer navigability/cohesion). Every other use case's Request/Response is namespaced `UseCases.{Name}`; consistency is the whole point of this cleanup. Since `AddMediatR` assembly-scan registration is namespace-independent (Decision above), this carries no functional risk.

#### Decision 3: Update controller and handler `using` directives per compiler feedback, do not guess blind
**Options considered:** (a) Pre-compute every `using` addition/removal by static analysis before touching a file; (b) move files first, then let `dotnet build` errors drive exactly which `using` directives to add/remove.
**Chosen approach:** (b), but informed by the static analysis already done in this review (below) to set expectations, not to skip verification.
**Rationale:** Grounded fact-finding for this review (grep across `Anela.Heblo.API/Controllers/PackingMaterialsController.cs`) confirms the controller's `using Anela.Heblo.Application.Features.PackingMaterials.Contracts;` is used *only* for the 8 types being moved — no other `Contracts` type (`PackingMaterialDto`, etc.) is referenced directly by the controller. So after the move that `using` line becomes fully unused and should be deleted, replaced with three new lines: `using ...UseCases.GetPackingMaterialsList;`, `using ...UseCases.CreatePackingMaterial;`, `using ...UseCases.UpdatePackingMaterial;` (the fourth, `using ...UseCases.UpdatePackingMaterialQuantity;`, is already present in the controller). This is a prediction to guide the implementer, not a substitute for running `dotnet build` and `dotnet format` after the move — a stray reference the grep missed (e.g. inside a comment, a partial class, or a generated file) would only surface there.

## Implementation Guidance

### Directory / Module Structure
As shown in Component Overview above. No new folders beyond the four `UseCases/{Name}/` folders that already exist (they currently hold only `*Handler.cs`).

### Interfaces and Contracts
No interface changes — `IRequest<TResponse>` and `BaseResponse` inheritance are preserved exactly as they are today; only the namespace of the concrete classes changes. Class names, property names, and property types are unchanged (verified by reading the current source of all 4 files — see spec FR-1..FR-4 for the exact per-class member lists already captured there).

### Data Flow
Unchanged. Controller constructs the Request via `ISender`/`IMediator` → MediatR resolves the handler by interface (namespace-independent) → handler returns the same Response type → controller returns it via `ActionResult<TResponse>`. No behavioral hop changes.

## Risks and Mitigations
| Risk | Severity | Mitigation |
|------|----------|------------|
| A consumer of these types outside the 10 files identified in spec FR-5 is missed (e.g. a mapping profile, a generated OpenAPI client reference, a Swagger doc-comment `<see cref>`) | Low | Run `dotnet build` for the whole solution (not just the Application project) after the move — any missed reference surfaces as a compile error, not a silent break, because these are compile-time C# types, not reflection-based lookups |
| Splitting `GetPackingMaterialsListRequest.cs`/`CreatePackingMaterialRequest.cs` into two files each introduces a typo or drops a member during the split | Low | Diff each new file's class body against the original combined file before deleting the original; do not retype members from memory |
| `dotnet format` reorders `using` directives or flags the now-stale `Contracts` import differently than expected | Low | Run `dotnet format` as part of validation (already required by repo rules) and accept its output rather than hand-ordering usings |
| OpenAPI-generated TypeScript client (frontend) accidentally changes because generation is namespace-sensitive somewhere unexpected | Low | Regenerate the TypeScript client per `docs/development/api-client-generation.md` and diff against the pre-change client — expect zero diff since routes/DTO shapes are unchanged; if a diff appears, treat it as a signal to investigate rather than commit blindly |

## Specification Amendments
Add to spec FR-1 and FR-2 explicitly: splitting the combined `GetPackingMaterialsListRequest.cs` and `CreatePackingMaterialRequest.cs` files into two files each (one class per file) is **required**, not optional — this was implicit in "as two files" language in the spec but is called out here as a hard requirement so the planner creates a task step for it rather than a naive "move file" operation.

Add to spec FR-5 acceptance criteria: after the move, remove the `using Anela.Heblo.Application.Features.PackingMaterials.Contracts;` line from `PackingMaterialsController.cs` (confirmed unused for any other purpose in that file) and add `using` directives for the three UseCases namespaces not already imported there (`GetPackingMaterialsList`, `CreatePackingMaterial`, `UpdatePackingMaterial`) — `UpdatePackingMaterialQuantity` is already imported.

## Prerequisites
None. No migrations, no config, no infrastructure changes. This can be implemented and merged independently of any other in-flight work in this module, since it touches no shared types and no behavior.
