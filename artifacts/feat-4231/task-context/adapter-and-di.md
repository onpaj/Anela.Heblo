### task: adapter-and-di

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/FileStorage/Infrastructure/ExpeditionListArchiveBlobStoreAdapter.cs`
- Modify: `backend/src/Anela.Heblo.Application/Features/FileStorage/FileStorageModule.cs`

- [ ] **Step 1: Create the adapter**

```csharp
// backend/src/Anela.Heblo.Application/Features/FileStorage/Infrastructure/ExpeditionListArchiveBlobStoreAdapter.cs
using Anela.Heblo.Application.Features.ExpeditionListArchive.Contracts;
using Anela.Heblo.Domain.Features.FileStorage;

namespace Anela.Heblo.Application.Features.FileStorage.Infrastructure;

/// <summary>
/// FileStorage-owned adapter implementing ExpeditionListArchive's IExpeditionListArchiveBlobStore
/// contract by delegating to the existing IBlobStorageService. Pure delegation/mapping — no
/// business logic. Mirrors KnowledgeBaseLeafletSourceAdapter's shape and visibility.
/// </summary>
internal sealed class ExpeditionListArchiveBlobStoreAdapter : IExpeditionListArchiveBlobStore
{
    private readonly IBlobStorageService _blobStorageService;

    public ExpeditionListArchiveBlobStoreAdapter(IBlobStorageService blobStorageService)
    {
        _blobStorageService = blobStorageService;
    }

    public Task<Stream> DownloadAsync(string containerName, string blobPath, CancellationToken cancellationToken)
        => _blobStorageService.DownloadAsync(containerName, blobPath, cancellationToken);

    public async Task<IReadOnlyList<ExpeditionBlobItem>> ListBlobsAsync(string containerName, string? prefix, CancellationToken cancellationToken)
    {
        var blobs = await _blobStorageService.ListBlobsAsync(containerName, prefix, cancellationToken);
        return blobs
            .Select(b => new ExpeditionBlobItem
            {
                Name = b.Name,
                FileName = b.FileName,
                CreatedOn = b.CreatedOn,
                ContentLength = b.ContentLength,
            })
            .ToList();
    }

    public Task<IReadOnlyList<string>> ListVirtualDirectoriesAsync(string containerName, CancellationToken cancellationToken)
        => _blobStorageService.ListVirtualDirectoriesAsync(containerName, cancellationToken);
}
```

- [ ] **Step 2: Register the DI binding in `FileStorageModule`**

Modify `backend/src/Anela.Heblo.Application/Features/FileStorage/FileStorageModule.cs`. Add the using statement and the registration line just before the method's `return services;`:

```csharp
using Anela.Heblo.Application.Features.ExpeditionListArchive.Contracts;
```
(add alongside the existing `using Anela.Heblo.Application.Features.FileStorage.Infrastructure;` line at the top of the file)

```csharp
        services.AddScoped<IValidator<DownloadFromUrlRequest>, DownloadFromUrlRequestValidator>();
        services.AddScoped<IPipelineBehavior<DownloadFromUrlRequest, DownloadFromUrlResponse>,
            ValidationResultBehavior<DownloadFromUrlRequest, DownloadFromUrlResponse>>();

        // Provider-owned binding for ExpeditionListArchive's consumer contract (cross-module
        // communication pattern — see docs/architecture/development_guidelines.md). Singleton
        // to match the wrapped IBlobStorageService's own Singleton lifetime; the adapter holds
        // no state of its own.
        services.AddSingleton<IExpeditionListArchiveBlobStore, ExpeditionListArchiveBlobStoreAdapter>();

        return services;
```

- [ ] **Step 3: Build to confirm it compiles**

Run: `cd backend && dotnet build src/Anela.Heblo.Application/Anela.Heblo.Application.csproj`
Expected: Build succeeded, 0 errors.

- [ ] **Step 4: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/FileStorage/Infrastructure/ExpeditionListArchiveBlobStoreAdapter.cs \
        backend/src/Anela.Heblo.Application/Features/FileStorage/FileStorageModule.cs
git commit -m "feat(file-storage): add ExpeditionListArchiveBlobStoreAdapter and register DI binding"
```

---

