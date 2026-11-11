# Architecture Review: Move DownloadFromUrl DTOs into FileStorage `Contracts/` folder

## Skip Design: true

## Architectural Fit Assessment

This is a pure namespace/folder relocation with zero behavioral change, and it aligns exactly with
established, already-repeated convention in this codebase — not just the written rule in
`development_guidelines.md` ("DTO objects for API (`Request`, `Response`) live in `contracts/`"), but
its concrete precedent: `Manufacture/Contracts/ConfirmProductCompletionRequest.cs` +
`ConfirmProductCompletionResponse.cs`, `Catalog/Contracts/GetCatalogListRequest.cs` +
`GetCatalogListResponse.cs`, `CatalogDocuments/Contracts/UploadDocumentResponse.cs`, and several
single-file `Contracts/*Request.cs` in `Journal`, `Marketing`, `PackingMaterials`. In each of these,
the MediatR `Request`/`Response` pair for a use case sits directly in `Contracts/`, sibling to the
`UseCases/{UseCase}/{UseCase}Handler.cs`, exactly as FR-1 specifies for `DownloadFromUrl`. FileStorage
is the outlier today: it is one of the few modules with only one use case and no `Contracts/` folder
at all, so its DTOs ended up flat inside `UseCases/DownloadFromUrl/` alongside the handler.

One observation worth surfacing (not a blocker, not in scope to fix here): `ProductExportDownloadJob`
in Catalog calling `_mediator.Send(new DownloadFromUrlRequest {...})` directly against another
module's MediatR request type is, as far as this review's codebase search found, the **only**
cross-module `_mediator.Send(new ...)` call in the entire Application layer — every other module
reached by name (`KnowledgeBase`, `Leaflet`, `Dashboard`, `Smartsupp`) only sends requests it owns
itself. Every other cross-module dependency in the codebase goes through a named service interface
(e.g. `IProductCatalogQueryService`, `ICatalogManufactureSource`) injected via DI, which is what
"Communication between modules exclusively through `contracts/` (e.g. `IProductQueryService`)" in the
guidelines is describing. Moving the DTOs to `Contracts/` does not change this: Catalog will still
reach into FileStorage's MediatR pipeline directly rather than through an interface. That is a
legitimate architectural question, but it is explicitly out of scope per the spec ("no logic changes")
and the brief only flags the folder placement, not the coupling mechanism — flagged here only as a
`Specification Amendments` note for a possible future issue, not something to act on now.

## Proposed Architecture

### Component Overview

```
Before:
  FileStorage/
    UseCases/DownloadFromUrl/
      DownloadFromUrlHandler.cs
      DownloadFromUrlRequest.cs     <- cross-module contract, buried in internal namespace
      DownloadFromUrlResponse.cs    <- cross-module contract, buried in internal namespace
    Validators/DownloadFromUrlRequestValidator.cs
    FileStorageModule.cs

  Catalog/Infrastructure/Jobs/ProductExportDownloadJob.cs
    --uses--> FileStorage.UseCases.DownloadFromUrl.{DownloadFromUrlRequest,DownloadFromUrlResponse}

After:
  FileStorage/
    Contracts/
      DownloadFromUrlRequest.cs     <- moved, namespace changed, no other change
      DownloadFromUrlResponse.cs    <- moved, namespace changed, no other change
    UseCases/DownloadFromUrl/
      DownloadFromUrlHandler.cs     <- stays; now references Contracts namespace
    Validators/DownloadFromUrlRequestValidator.cs  <- references Contracts namespace
    FileStorageModule.cs                            <- references Contracts namespace

  Catalog/Infrastructure/Jobs/ProductExportDownloadJob.cs
    --uses--> FileStorage.Contracts.{DownloadFromUrlRequest,DownloadFromUrlResponse}

  API/Controllers/FileStorageController.cs
    --uses--> FileStorage.Contracts.{DownloadFromUrlRequest,DownloadFromUrlResponse}
```

No component gains or loses a responsibility. The handler, validator, module registration,
controller action, and the Hangfire job keep doing exactly what they do today; only the `using`
directive (and, for the two moved files, the `namespace` declaration) changes.

### Key Design Decisions

#### Decision 1: Where the moved files land, and how the handler folder is left

**Options considered:**
1. Move both DTOs into a new `FileStorage/Contracts/` folder, leave the handler in
   `UseCases/DownloadFromUrl/`.
2. Move the DTOs *and* the handler together into a flattened `FileStorage/` layout (matching the
   "Simple Features (1-3 use cases)" pattern in `filesystem.md`, which uses a `Model/` folder instead
   of `UseCases/*/`).
3. Leave everything as-is and only document the exception.

**Chosen approach:** Option 1, matching FR-1/FR-2 exactly as specified.

**Rationale:** `filesystem.md`'s "Simple Features" pattern (`Model/` folder) and its "Complex
Features" pattern (`UseCases/` + `Contracts/`) are two alternative conventions for different module
sizes — switching FileStorage from one to the other is a bigger restructuring than this issue asks
for, would touch the handler's own file (spec's Out-of-Scope explicitly forbids that), and isn't
needed to satisfy the actual guideline being enforced (contract DTOs out of the internal-implementation
namespace). Option 1 is the minimal change that fixes exactly the finding: it produces the same shape
already used by `Manufacture` (`Contracts/ConfirmProductCompletionRequest.cs` +
`UseCases/ConfirmProductCompletion/ConfirmProductCompletionHandler.cs`), so it is not a new pattern —
it is FileStorage catching up to a pattern already proven twice over in this codebase.

#### Decision 2: Namespace of the moved files

**Options considered:**
1. `Anela.Heblo.Application.Features.FileStorage.Contracts`
2. Keep `Anela.Heblo.Application.Features.FileStorage.UseCases.DownloadFromUrl` and only move the
   physical file location (namespace/folder mismatch).

**Chosen approach:** Option 1 — namespace matches folder path, per FR-1.

**Rationale:** Every existing `Contracts/` folder in this codebase uses
`Anela.Heblo.Application.Features.{Feature}.Contracts` as the namespace (confirmed against
`Catalog.Contracts`, `Manufacture.Contracts`, `Journal.Contracts`). A folder/namespace mismatch would
be a novel inconsistency this refactor should not introduce.

## Implementation Guidance

### Directory / Module Structure

Create:
- `backend/src/Anela.Heblo.Application/Features/FileStorage/Contracts/DownloadFromUrlRequest.cs`
- `backend/src/Anela.Heblo.Application/Features/FileStorage/Contracts/DownloadFromUrlResponse.cs`

Delete (after move):
- `backend/src/Anela.Heblo.Application/Features/FileStorage/UseCases/DownloadFromUrl/DownloadFromUrlRequest.cs`
- `backend/src/Anela.Heblo.Application/Features/FileStorage/UseCases/DownloadFromUrl/DownloadFromUrlResponse.cs`

Unchanged location, `using`/namespace-reference only:
- `.../FileStorage/UseCases/DownloadFromUrl/DownloadFromUrlHandler.cs`
- `.../FileStorage/Validators/DownloadFromUrlRequestValidator.cs`
- `.../FileStorage/FileStorageModule.cs`
- `.../Anela.Heblo.Application/Features/Catalog/Infrastructure/Jobs/ProductExportDownloadJob.cs`
- `backend/src/Anela.Heblo.API/Controllers/FileStorageController.cs`

Test files needing only a `using` swap (all confirmed present and referencing the old namespace via
grep):
- `backend/test/Anela.Heblo.Tests/Features/FileStorage/DownloadFromUrlHandlerTests.cs`
- `backend/test/Anela.Heblo.Tests/Features/FileStorage/FileStorageControllerTests.cs`
- `backend/test/Anela.Heblo.Tests/Features/FileStorage/Pipeline/FileStorageValidationPipelineTests.cs`
- `backend/test/Anela.Heblo.Tests/Features/FileStorage/Validators/DownloadFromUrlRequestValidatorTests.cs`
- `backend/test/Anela.Heblo.Tests/Features/Catalog/Infrastructure/Jobs/ProductExportDownloadJobTests.cs`

No other file in the repository references the old namespace (verified via
`grep -rl "DownloadFromUrl"` across `backend/`) beyond the ones already listed in the spec's FR-1–FR-5
and this list — the reference set is complete, no additional callers were found.

### Interfaces and Contracts

No interface or contract shape changes. Both types keep their exact members and inheritance:

```csharp
// Anela.Heblo.Application.Features.FileStorage.Contracts.DownloadFromUrlRequest
public class DownloadFromUrlRequest : IRequest<DownloadFromUrlResponse>
{
    [Required] public string FileUrl { get; set; } = null!;
    [Required] public string ContainerName { get; set; } = null!;
    public string? BlobName { get; set; }
}

// Anela.Heblo.Application.Features.FileStorage.Contracts.DownloadFromUrlResponse
public class DownloadFromUrlResponse : BaseResponse
{
    public string BlobUrl { get; set; } = null!;
    public string BlobName { get; set; } = null!;
    public string ContainerName { get; set; } = null!;
    public long FileSizeBytes { get; set; }
}
```

MediatR resolves `IRequestHandler<DownloadFromUrlRequest, DownloadFromUrlResponse>` by type, not by
namespace, so `FileStorageModule.AddFileStorageModule`'s `AddMediatR` scan and the explicit
`IValidator<DownloadFromUrlRequest>` / `IPipelineBehavior<DownloadFromUrlRequest,
DownloadFromUrlResponse>` registrations continue to work unchanged once their `using` directives point
at `Contracts`.

### Data Flow

Unchanged. `FileStorageController.DownloadFromUrl` → `IMediator.Send` → `ValidationResultBehavior`
(using `DownloadFromUrlRequestValidator`) → `DownloadFromUrlHandler` → `IBlobStorageService` /
`IDownloadResilienceService`. Same for the Catalog path:
`ProductExportDownloadJob.ExecuteAsync` → `IMediator.Send(new DownloadFromUrlRequest {...})` → same
pipeline. Only the compile-time type reference for `DownloadFromUrlRequest`/`DownloadFromUrlResponse`
moves; the runtime call graph is identical before and after.

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| Missed reference causes a build break | Low | `grep -rl "UseCases.DownloadFromUrl"` (or a full `dotnet build`) after the move will surface any straggler immediately; the full reference set was enumerated above and matches the spec's FR-1–FR-5 exactly. |
| OpenAPI-generated TS client (`api-client.ts`) picks up a cosmetic schema/type-name diff from the namespace change | Low | Per spec FR-4/NFR-3, regenerate the client as part of the normal build and diff it; the public HTTP route/JSON shape is unaffected, so any diff should be limited to internal generator artifacts, if that. |
| `git mv` not used, losing file history on the two moved files | Low | Use `git mv` (or add+delete recognized by Git's rename detection) rather than delete+recreate, to preserve blame/history for `DownloadFromUrlRequest.cs`/`DownloadFromUrlResponse.cs`. |

No risk here rises above Low — this is a compile-time-only, single-repo, fully-enumerated rename.

## Specification Amendments

None required to FR-1 through FR-5 — they are implementable as written and match existing convention
precisely (see `Manufacture/Contracts/ConfirmProductCompletionRequest.cs` /
`ConfirmProductCompletionResponse.cs` as the closest structural precedent).

One note for a possible **future, separate** issue, not this one: `ProductExportDownloadJob` reaching
into FileStorage's MediatR request type directly (rather than through a dedicated interface such as an
`IFileDownloadService`) is the only direct cross-module MediatR `Send` in the codebase; every other
cross-module dependency found in this review goes through a DI-injected interface contract. This
refactor does not change that coupling — it only relocates the DTO within FileStorage — and the spec
correctly keeps it out of scope. Flagging it here only so it isn't lost.

## Prerequisites

None. No migration, config, or infrastructure change is needed — this is a source-only rename that
compiles and deploys like any other commit. Implementation can start immediately.
