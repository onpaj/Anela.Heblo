# Design: Move DownloadFromUrl DTOs into FileStorage Contracts/ folder

## Component Design

This is a namespace/folder relocation only — no component gains, loses, or changes a responsibility.

### `FileStorage/Contracts/` (new)
Public, cross-module-callable surface of the FileStorage module. Sibling to `UseCases/`, matching the
existing pattern in `Manufacture`, `Catalog`, `CatalogDocuments`, `Journal`, `Marketing`, and
`PackingMaterials`.

- `DownloadFromUrlRequest.cs` — moved from `UseCases/DownloadFromUrl/`, namespace changed from
  `Anela.Heblo.Application.Features.FileStorage.UseCases.DownloadFromUrl` to
  `Anela.Heblo.Application.Features.FileStorage.Contracts`. No member, attribute, or base-type change.
- `DownloadFromUrlResponse.cs` — same move, same namespace change, no member change.

### `FileStorage/UseCases/DownloadFromUrl/DownloadFromUrlHandler.cs` (unchanged location)
Stays exactly where it is — an internal implementation detail, not a contract. Only its `using`
directive is repointed at `FileStorage.Contracts`. `IRequestHandler<DownloadFromUrlRequest,
DownloadFromUrlResponse>` resolution is unaffected: MediatR binds by type, not namespace.

### Referencing components (using/namespace-reference update only)
No behavior, signature, or call-site change in any of these — only the `using` directive (or a fully
qualified reference) is repointed from `...UseCases.DownloadFromUrl` to `...FileStorage.Contracts`:

- `FileStorage/Validators/DownloadFromUrlRequestValidator.cs` — validation rules unchanged.
- `FileStorage/FileStorageModule.cs` — DI/pipeline registration unchanged (MediatR scan, `IValidator<T>`,
  `IPipelineBehavior<T,R>` registrations resolve identically once the type is imported from its new
  namespace).
- `Catalog/Infrastructure/Jobs/ProductExportDownloadJob.cs` — the sole cross-module caller; its
  `_mediator.Send(new DownloadFromUrlRequest {...})` call site and field assignments (`FileUrl`,
  `ContainerName`, `BlobName`) are byte-for-byte unchanged.
- `API/Controllers/FileStorageController.cs` — endpoint route, action signature, and behavior unchanged.

### Data flow (unchanged)
`FileStorageController.DownloadFromUrl` → `IMediator.Send` → `ValidationResultBehavior`
(`DownloadFromUrlRequestValidator`) → `DownloadFromUrlHandler` → `IBlobStorageService` /
`IDownloadResilienceService`. Catalog path: `ProductExportDownloadJob.ExecuteAsync` →
`IMediator.Send(new DownloadFromUrlRequest {...})` → same pipeline. The runtime call graph is identical
before and after; only the compile-time type reference moves.

### Test files (using-only update)
- `test/.../Features/FileStorage/DownloadFromUrlHandlerTests.cs`
- `test/.../Features/FileStorage/FileStorageControllerTests.cs`
- `test/.../Features/FileStorage/Pipeline/FileStorageValidationPipelineTests.cs`
- `test/.../Features/FileStorage/Validators/DownloadFromUrlRequestValidatorTests.cs`
- `test/.../Features/Catalog/Infrastructure/Jobs/ProductExportDownloadJobTests.cs`

No test assertion, test data, or test behavior changes.

## Data Schemas

No schema, JSON shape, or database change. Both DTOs keep their exact members and inheritance —
only the containing namespace changes.

```csharp
// Before: Anela.Heblo.Application.Features.FileStorage.UseCases.DownloadFromUrl.DownloadFromUrlRequest
// After:  Anela.Heblo.Application.Features.FileStorage.Contracts.DownloadFromUrlRequest
public class DownloadFromUrlRequest : IRequest<DownloadFromUrlResponse>
{
    [Required] public string FileUrl { get; set; } = null!;
    [Required] public string ContainerName { get; set; } = null!;
    public string? BlobName { get; set; }
}

// Before: Anela.Heblo.Application.Features.FileStorage.UseCases.DownloadFromUrl.DownloadFromUrlResponse
// After:  Anela.Heblo.Application.Features.FileStorage.Contracts.DownloadFromUrlResponse
public class DownloadFromUrlResponse : BaseResponse
{
    public string BlobUrl { get; set; } = null!;
    public string BlobName { get; set; } = null!;
    public string ContainerName { get; set; } = null!;
    public long FileSizeBytes { get; set; }
    // inherited from BaseResponse: Success, ErrorCode, Params, ...
}
```

No public HTTP request/response JSON shape change. The controller route and payload contract exposed
by `FileStorageController` are unaffected; the OpenAPI-generated TypeScript client
(`frontend/src/api/generated/api-client.ts`) is regenerated as part of the normal build and should show
no shape diff (an internal generator type-name artifact, if any, is acceptable per spec NFR-3/FR-4).
