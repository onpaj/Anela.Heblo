# Design: Decouple ExpeditionListArchive from FileStorage's IBlobStorageService

## Component Design

### `IExpeditionListArchiveBlobStore` (new, consumer-owned contract)
- **Location**: `Anela.Heblo.Application.Features.ExpeditionListArchive.Contracts`
- **Responsibility**: The only blob-storage surface `ExpeditionListArchive` is allowed to depend on. Exposes exactly 3 operations, matching the union of what the module's 4 handlers actually call today.
- **Members**:
  - `Task<Stream> DownloadAsync(string containerName, string blobPath, CancellationToken cancellationToken)`
  - `Task<IReadOnlyList<ExpeditionBlobItem>> ListBlobsAsync(string containerName, string? prefix, CancellationToken cancellationToken)`
  - `Task<IReadOnlyList<string>> ListVirtualDirectoriesAsync(string containerName, CancellationToken cancellationToken)`
- **Consumers**: `DownloadExpeditionListHandler`, `ReprintExpeditionListHandler` (both use `DownloadAsync`), `GetExpeditionListsByDateHandler` (`ListBlobsAsync`), `GetExpeditionDatesHandler` (`ListVirtualDirectoriesAsync`).

### `ExpeditionBlobItem` (new, consumer-owned DTO)
- **Location**: `Anela.Heblo.Application.Features.ExpeditionListArchive.Contracts`
- **Responsibility**: Replaces `Anela.Heblo.Domain.Features.FileStorage.BlobItemInfo` at the `ExpeditionListArchive` boundary. Field-for-field identical shape (`Name`, `FileName`, `CreatedOn`, `ContentLength`) so `GetExpeditionListsByDateHandler`'s existing projection logic requires no changes beyond the source type.

### `ExpeditionListArchiveBlobStoreAdapter` (new, provider-owned adapter)
- **Location**: `Anela.Heblo.Application.Features.FileStorage.Infrastructure` (`internal sealed class`, per arch-review Decision 1)
- **Responsibility**: Implements `IExpeditionListArchiveBlobStore` by delegating each call 1:1 to the existing `IBlobStorageService`, mapping `BlobItemInfo` → `ExpeditionBlobItem` for `ListBlobsAsync`. Contains no business logic — pure delegation/mapping, exactly like `KnowledgeBaseLeafletSourceAdapter`.
- **Dependencies**: `IBlobStorageService` (constructor-injected).
- **Lifetime**: `Singleton` (arch-review Decision 3), registered in `FileStorageModule.AddFileStorageModule`.

### The four `ExpeditionListArchive` handlers (modified, not new)
- **`DownloadExpeditionListHandler`**: constructor dependency changes from `IBlobStorageService` to `IExpeditionListArchiveBlobStore`; body unchanged (still validates `BlobPathValidator.IsValid`, still calls `DownloadAsync(_containerName, request.BlobPath, cancellationToken)`).
- **`ReprintExpeditionListHandler`**: same constructor dependency swap; body unchanged (validate → download → temp file → print sink → cleanup). Its manual DI factory registration in `ExpeditionListArchiveModule.cs` is updated in the same change to resolve `IExpeditionListArchiveBlobStore` instead of `IBlobStorageService`.
- **`GetExpeditionListsByDateHandler`**: constructor dependency swap; `ListBlobsAsync` return type changes from `IReadOnlyList<BlobItemInfo>` to `IReadOnlyList<ExpeditionBlobItem>`, consumed by the same `.Where(...).Select(...)` projection into `ExpeditionListItemDto` (property names identical, no logic change).
- **`GetExpeditionDatesHandler`**: constructor dependency swap; `ListVirtualDirectoriesAsync` call unchanged in shape.

### `ModuleBoundariesTests` (modified, not new)
- One new `ModuleBoundaryRule` entry added to the existing `Rules()` `TheoryData`: `"ExpeditionListArchive -> FileStorage"`, inspecting `Anela.Heblo.Application.Features.ExpeditionListArchive`, forbidding `Anela.Heblo.Domain.Features.FileStorage` / `Anela.Heblo.Application.Features.FileStorage` / `Anela.Heblo.Persistence.FileStorage`, empty allowlist. This is the regression guard that makes the fix permanent — no code changes to the test *engine*, only a new data row.

## Data Schemas

No database schema changes. No public HTTP API request/response shape changes (all existing `*Request`/`*Response` DTOs for the four use cases — `DownloadExpeditionListRequest/Response`, `ReprintExpeditionListRequest/Response`, `GetExpeditionListsByDateRequest/Response`, `GetExpeditionDatesRequest/Response`, and `ExpeditionListItemDto` — are unchanged).

### New internal contract shapes

```csharp
namespace Anela.Heblo.Application.Features.ExpeditionListArchive.Contracts;

public interface IExpeditionListArchiveBlobStore
{
    Task<Stream> DownloadAsync(string containerName, string blobPath, CancellationToken cancellationToken);
    Task<IReadOnlyList<ExpeditionBlobItem>> ListBlobsAsync(string containerName, string? prefix, CancellationToken cancellationToken);
    Task<IReadOnlyList<string>> ListVirtualDirectoriesAsync(string containerName, CancellationToken cancellationToken);
}

public class ExpeditionBlobItem
{
    public string Name { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public DateTimeOffset? CreatedOn { get; set; }
    public long? ContentLength { get; set; }
}
```

### DI registration shape (added to `FileStorageModule.AddFileStorageModule`)

```csharp
services.AddSingleton<IExpeditionListArchiveBlobStore, ExpeditionListArchiveBlobStoreAdapter>();
```

### Field-mapping table (adapter's `ListBlobsAsync`)

| `BlobItemInfo` (source, FileStorage domain) | `ExpeditionBlobItem` (target, ExpeditionListArchive contract) |
|---|---|
| `Name` (`string`) | `Name` (`string`) |
| `FileName` (`string`) | `FileName` (`string`) |
| `CreatedOn` (`DateTimeOffset?`) | `CreatedOn` (`DateTimeOffset?`) |
| `ContentLength` (`long?`) | `ContentLength` (`long?`) |

1:1, no transformation, no dropped or renamed fields.
