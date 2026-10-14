### task: contracts-and-dto

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/ExpeditionListArchive/Contracts/IExpeditionListArchiveBlobStore.cs`
- Create: `backend/src/Anela.Heblo.Application/Features/ExpeditionListArchive/Contracts/ExpeditionBlobItem.cs`

- [ ] **Step 1: Create the `ExpeditionBlobItem` DTO**

```csharp
// backend/src/Anela.Heblo.Application/Features/ExpeditionListArchive/Contracts/ExpeditionBlobItem.cs
namespace Anela.Heblo.Application.Features.ExpeditionListArchive.Contracts;

/// <summary>
/// ExpeditionListArchive-owned metadata about a single archived expedition list blob.
/// Structurally mirrors Anela.Heblo.Domain.Features.FileStorage.BlobItemInfo, but is owned by
/// this module so ExpeditionListArchive never references the FileStorage domain directly.
/// </summary>
public class ExpeditionBlobItem
{
    public string Name { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public DateTimeOffset? CreatedOn { get; set; }
    public long? ContentLength { get; set; }
}
```

- [ ] **Step 2: Create the `IExpeditionListArchiveBlobStore` contract**

```csharp
// backend/src/Anela.Heblo.Application/Features/ExpeditionListArchive/Contracts/IExpeditionListArchiveBlobStore.cs
namespace Anela.Heblo.Application.Features.ExpeditionListArchive.Contracts;

/// <summary>
/// ExpeditionListArchive-owned narrow contract over blob storage. Exposes only the three
/// operations this module actually needs, implemented by a FileStorage-owned adapter
/// (see Anela.Heblo.Application.Features.FileStorage.Infrastructure.ExpeditionListArchiveBlobStoreAdapter).
/// Do not add speculative methods here — extend only when a handler in this module needs them.
/// </summary>
public interface IExpeditionListArchiveBlobStore
{
    Task<Stream> DownloadAsync(string containerName, string blobPath, CancellationToken cancellationToken);

    Task<IReadOnlyList<ExpeditionBlobItem>> ListBlobsAsync(string containerName, string? prefix, CancellationToken cancellationToken);

    Task<IReadOnlyList<string>> ListVirtualDirectoriesAsync(string containerName, CancellationToken cancellationToken);
}
```

- [ ] **Step 3: Build to confirm the new files compile**

Run: `cd backend && dotnet build src/Anela.Heblo.Application/Anela.Heblo.Application.csproj`
Expected: Build succeeded, 0 errors (the two new files have no dependents yet, so nothing else changes).

- [ ] **Step 4: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/ExpeditionListArchive/Contracts/IExpeditionListArchiveBlobStore.cs \
        backend/src/Anela.Heblo.Application/Features/ExpeditionListArchive/Contracts/ExpeditionBlobItem.cs
git commit -m "feat(expedition-list-archive): add IExpeditionListArchiveBlobStore contract and ExpeditionBlobItem DTO"
```

---

