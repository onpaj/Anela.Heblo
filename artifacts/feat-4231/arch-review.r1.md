# Architecture Review: Decouple ExpeditionListArchive from FileStorage's IBlobStorageService

## Skip Design: true

## Architectural Fit Assessment
This is a pure internal-dependency refactor with no UI, no API contract change, and no data-model change. It fits an established, repeated pattern in this codebase: a consumer module depends on a full provider-owned service interface instead of a narrow, consumer-owned contract. The canonical fix (`ILeafletKnowledgeSource` / `KnowledgeBaseLeafletSourceAdapter`) is already documented in `docs/architecture/development_guidelines.md` under "Cross-Module Communication Example", and at least half a dozen other module pairs in this codebase already follow the identical shape (`IManufactureCatalogSource`, `IOrderStatusReader`, `ILogisticsStockOperationQueryService`, `IShipmentDeliveryChecker`, `IPackedOrderStatusUpdater`, etc. — all visible as `ModuleBoundaryRule` entries in `ModuleBoundariesTests.cs`). No new architectural pattern is being introduced; this is applying an existing, well-tested pattern to a module pair that was missed.

I resolved the spec's one open question by reading the actual module wiring:
- `Anela.Heblo.Application.Features.FileStorage.FileStorageModule` (`AddFileStorageModule`) already exists as the Application-layer home for the `FileStorage` module, already has an `Infrastructure/` subfolder (`DownloadResilienceService`), and is called from `ApplicationModule.cs` (line 90).
- `ExpeditionListArchiveModule.AddExpeditionListArchiveModule` is called from the same `ApplicationModule.cs` (line 110).
- `IBlobStorageService` itself is bound separately, in `AzureAdapterModule.AddAzureBlobStorageService` (`backend/src/Adapters/Anela.Heblo.Adapters.Azure/AzureAdapterModule.cs`), called from `Program.cs` **after** `AddApplicationServices`. This registration-order difference is irrelevant: ASP.NET Core's `IServiceCollection` only requires all registrations to exist before the container is *built* (first resolution), not before each other — a factory delegate registered in `FileStorageModule` that calls `provider.GetRequiredService<IBlobStorageService>()` resolves correctly regardless of which `Add*` call ran first, as long as both ran before the app starts serving requests (they do — both are in `Program.cs`'s single synchronous startup sequence).

This confirms the adapter belongs in the **Application-layer `FileStorage` module**, not the Azure Adapters project — mirroring `KnowledgeBaseLeafletSourceAdapter`'s placement (Application-layer provider module's `Infrastructure/`, not the persistence/adapter project), and keeping the `FileStorage` Application module — not the Azure-specific adapter project — as the seam that owns "which concrete blob backend implements which contracts for which consumers." This also decouples the binding from Azure specifically: if `IBlobStorageService`'s implementation ever changes (e.g., a different provider is swapped in during tests or a future migration), `FileStorageModule`'s registration of `IExpeditionListArchiveBlobStore` requires no change, because it only depends on the already-registered `IBlobStorageService` abstraction, not on `AzureBlobStorageService` directly.

## Proposed Architecture

### Component Overview

```
Before:
  ExpeditionListArchive handlers (4×) ──depends on──> IBlobStorageService (FileStorage Domain, 7 methods)
                                                              ▲
                                                              │ implements
                                                        AzureBlobStorageService (Azure Adapters)

After:
  ExpeditionListArchive handlers (4×) ──depends on──> IExpeditionListArchiveBlobStore (ExpeditionListArchive.Contracts, 3 methods)
                                                              ▲
                                                              │ implements
                                            ExpeditionListArchiveBlobStoreAdapter (FileStorage.Infrastructure)
                                                              │ delegates to
                                                              ▼
                                                        IBlobStorageService (FileStorage Domain, unchanged)
                                                              ▲
                                                              │ implements
                                                        AzureBlobStorageService (Azure Adapters, unchanged)
```

This is structurally identical to the existing `ILeafletKnowledgeSource → KnowledgeBaseLeafletSourceAdapter → IKnowledgeBaseRepository` chain — same three-layer shape (consumer contract → provider adapter → provider's own internal service).

### Key Design Decisions

#### Decision 1: Adapter location — Application-layer `FileStorage.Infrastructure`, not the Azure Adapters project
**Options considered:**
1. `backend/src/Adapters/Anela.Heblo.Adapters.Azure/Features/FileStorage/` (next to `AzureBlobStorageService`).
2. `backend/src/Anela.Heblo.Application/Features/FileStorage/Infrastructure/` (Application-layer, alongside `DownloadResilienceService`).

**Chosen approach:** Option 2.

**Rationale:** The canonical example (`KnowledgeBaseLeafletSourceAdapter`) lives in the Application layer's provider module (`KnowledgeBase.Infrastructure`), not in a separate adapters/infra project — the "provider module" in the documented pattern means the *feature* module that owns the capability (`FileStorage`), not the concrete cloud-vendor adapter project (`Adapters.Azure`). Placing it in `Adapters.Azure` would make the binding Azure-specific for no reason: the adapter only needs `IBlobStorageService`, an abstraction already available in the Application layer, so it has no reason to sit in a project whose only job is providing concrete Azure SDK bindings. This also keeps `Anela.Heblo.Application.Features.FileStorage` as the single place that owns "what `FileStorage` exposes to other modules," consistent with how every other module in this codebase organizes its consumer-facing adapters.

#### Decision 2: DI binding registration site — `FileStorageModule.AddFileStorageModule`
**Options considered:**
1. Register in `ExpeditionListArchiveModule.AddExpeditionListArchiveModule` (the consumer's own module).
2. Register in `FileStorageModule.AddFileStorageModule` (the provider's module).

**Chosen approach:** Option 2, matching `KnowledgeBaseModule.AddKnowledgeBaseModule` registering `ILeafletKnowledgeSource`'s binding — the provider module owns the binding, never the consumer. The consumer module (`ExpeditionListArchiveModule`) must not know or care what concretely implements its own contract.

#### Decision 3: DI lifetime — Singleton
**Options considered:** `Singleton` (matching the wrapped `IBlobStorageService`'s own Singleton lifetime) vs. `Scoped`.

**Chosen approach:** `Singleton`. The adapter is a pure stateless delegation/mapping wrapper around a Singleton dependency — there is no scoped state anywhere in the chain (`IBlobStorageService` itself is Singleton specifically to preserve its internal `_containerExists` cache across requests, per the comment in `AzureAdapterModule`). Registering the adapter as `Scoped` would work functionally (a Scoped service may depend on a Singleton) but adds no value and is inconsistent with the dependency it wraps 1:1. `Singleton` is simpler, has zero downside here, and avoids any future confusion about why a stateless wrapper would need per-request lifetime.

#### Decision 4: `ReprintExpeditionListHandler`'s manual factory in `ExpeditionListArchiveModule.cs`
**Options considered:** Leave the existing manual `services.AddTransient<IRequestHandler<...>>(provider => ...)` factory untouched and have it resolve `IBlobStorageService` internally (defeating the whole point), vs. update the factory to resolve the new `IExpeditionListArchiveBlobStore`.

**Chosen approach:** Update the factory. This factory exists solely to pick the keyed `"cups"` `IPrintQueueSink` when present — it is orthogonal to the blob-storage dependency and must be updated in lockstep with the constructor signature change, or the module will fail to compile (constructor argument type mismatch). This is a direct, mechanical edit: `provider.GetRequiredService<IBlobStorageService>()` → `provider.GetRequiredService<IExpeditionListArchiveBlobStore>()`.

## Implementation Guidance

### Directory / Module Structure

New files:
```
backend/src/Anela.Heblo.Application/Features/ExpeditionListArchive/Contracts/
  IExpeditionListArchiveBlobStore.cs   (new)
  ExpeditionBlobItem.cs                (new)

backend/src/Anela.Heblo.Application/Features/FileStorage/Infrastructure/
  ExpeditionListArchiveBlobStoreAdapter.cs   (new)
```

Modified files:
```
backend/src/Anela.Heblo.Application/Features/FileStorage/FileStorageModule.cs
  — add: services.AddSingleton<IExpeditionListArchiveBlobStore, ExpeditionListArchiveBlobStoreAdapter>();

backend/src/Anela.Heblo.Application/Features/ExpeditionListArchive/ExpeditionListArchiveModule.cs
  — the ReprintExpeditionListHandler factory resolves IExpeditionListArchiveBlobStore instead of IBlobStorageService
  — remove `using Anela.Heblo.Domain.Features.FileStorage;`
  — add `using Anela.Heblo.Application.Features.ExpeditionListArchive.Contracts;` (if not already present via ExpeditionListItemDto usage elsewhere — confirm per-file)

backend/src/Anela.Heblo.Application/Features/ExpeditionListArchive/UseCases/DownloadExpeditionList/DownloadExpeditionListHandler.cs
backend/src/Anela.Heblo.Application/Features/ExpeditionListArchive/UseCases/ReprintExpeditionList/ReprintExpeditionListHandler.cs
backend/src/Anela.Heblo.Application/Features/ExpeditionListArchive/UseCases/GetExpeditionListsByDate/GetExpeditionListsByDateHandler.cs
backend/src/Anela.Heblo.Application/Features/ExpeditionListArchive/UseCases/GetExpeditionDates/GetExpeditionDatesHandler.cs
  — swap IBlobStorageService → IExpeditionListArchiveBlobStore in constructor + field + using statements

backend/test/Anela.Heblo.Tests/ExpeditionListArchive/DownloadExpeditionListHandlerTests.cs
backend/test/Anela.Heblo.Tests/ExpeditionListArchive/ReprintExpeditionListHandlerTests.cs
backend/test/Anela.Heblo.Tests/ExpeditionListArchive/GetExpeditionListsByDateHandlerTests.cs
backend/test/Anela.Heblo.Tests/ExpeditionListArchive/GetExpeditionDatesHandlerTests.cs
  — Mock<IBlobStorageService> → Mock<IExpeditionListArchiveBlobStore>; BlobItemInfo → ExpeditionBlobItem in GetExpeditionListsByDateHandlerTests.cs

backend/test/Anela.Heblo.Tests/Architecture/ModuleBoundariesTests.cs
  — add one new ModuleBoundaryRule ("ExpeditionListArchive -> FileStorage") to the Rules() TheoryData
```

No changes needed to: `Program.cs`, `ApplicationModule.cs`, `AzureAdapterModule.cs`, `IBlobStorageService.cs`, `AzureBlobStorageService.cs`, `BlobPathValidator.cs`, any Request/Response DTOs, or `ExpeditionListItemDto.cs`.

### Interfaces and Contracts

```csharp
// Anela.Heblo.Application.Features.ExpeditionListArchive.Contracts.IExpeditionListArchiveBlobStore
public interface IExpeditionListArchiveBlobStore
{
    Task<Stream> DownloadAsync(string containerName, string blobPath, CancellationToken cancellationToken);
    Task<IReadOnlyList<ExpeditionBlobItem>> ListBlobsAsync(string containerName, string? prefix, CancellationToken cancellationToken);
    Task<IReadOnlyList<string>> ListVirtualDirectoriesAsync(string containerName, CancellationToken cancellationToken);
}

// Anela.Heblo.Application.Features.ExpeditionListArchive.Contracts.ExpeditionBlobItem
public class ExpeditionBlobItem
{
    public string Name { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public DateTimeOffset? CreatedOn { get; set; }
    public long? ContentLength { get; set; }
}
```

```csharp
// Anela.Heblo.Application.Features.FileStorage.Infrastructure.ExpeditionListArchiveBlobStoreAdapter
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

Follow `internal sealed class` visibility exactly as `KnowledgeBaseLeafletSourceAdapter` does — the adapter is an implementation detail of `FileStorage`'s DI wiring, never referenced by name outside `FileStorageModule.cs`.

### Data Flow
Unchanged at runtime, only the types on the wire between handler and blob backend change:
1. Handler calls `IExpeditionListArchiveBlobStore.{DownloadAsync|ListBlobsAsync|ListVirtualDirectoriesAsync}`.
2. DI resolves `ExpeditionListArchiveBlobStoreAdapter` (Singleton), which holds a reference to the Singleton `IBlobStorageService` (`AzureBlobStorageService`).
3. Adapter calls the corresponding `IBlobStorageService` method with the same arguments, unwrapped.
4. For `ListBlobsAsync`, the adapter maps `IReadOnlyList<BlobItemInfo>` → `IReadOnlyList<ExpeditionBlobItem>` before returning; `DownloadAsync` and `ListVirtualDirectoriesAsync` pass the result through unchanged (stream / string list).
5. Handler logic (validation, filtering `.pdf`, pagination, sorting, error codes) is untouched.

## Risks and Mitigations
| Risk | Severity | Mitigation |
|------|----------|------------|
| `ReprintExpeditionListHandler`'s manual DI factory in `ExpeditionListArchiveModule.cs` is easy to miss since it's not a normal constructor-injection site | Medium | Explicitly called out in Implementation Guidance and FR-5; a build error (constructor mismatch) will surface immediately if missed, so this cannot silently regress |
| Forgetting to update `GetExpeditionListsByDateHandlerTests.cs`'s `BlobItemInfo` fixtures to `ExpeditionBlobItem` | Low | Compile error — `Mock<IExpeditionListArchiveBlobStore>.Setup(...ListBlobsAsync...)` will not accept `BlobItemInfo` return values, so this is caught at build time, not runtime |
| New `ModuleBoundaryRule` test could reveal an unexpected additional reference not covered by this plan (e.g., a compiler-generated closure capturing `BlobItemInfo`) | Low | Run the new/updated `ModuleBoundariesTests` theory locally before considering the task done; the test's failure message enumerates every violating reference by name, making any residual leak trivial to find and fix |
| `Singleton` lifetime chosen for the adapter turns out to be wrong if a future change adds scoped state to `ExpeditionListArchiveBlobStoreAdapter` | Low | The adapter is documented as a pure stateless wrapper (Decision 3); any future addition of scoped/request state would be a deliberate, reviewable change to this specific class |

## Specification Amendments
- **FR-3 / Open Questions resolved**: adapter class is `Anela.Heblo.Application.Features.FileStorage.Infrastructure.ExpeditionListArchiveBlobStoreAdapter` (Application-layer `FileStorage` module, `internal sealed class`), **not** in the Azure Adapters project.
- **FR-4 amended**: the DI binding (`services.AddSingleton<IExpeditionListArchiveBlobStore, ExpeditionListArchiveBlobStoreAdapter>();`) is registered inside `FileStorageModule.AddFileStorageModule`, immediately after the existing `services.Configure<FileDownloadOptions>(...)` line (or any point after the method's existing registrations — order within the method does not matter for a factory-free `AddSingleton<TI, TImpl>` binding).
- **NFR-3 resolved**: lifetime is `Singleton` (Decision 3) — spec's "Scoped is also safe" hedge is superseded; use `Singleton` for consistency with the wrapped dependency and because the adapter is provably stateless.

## Prerequisites
None — no infrastructure, migration, or configuration changes are required before implementation starts. All dependent types (`IBlobStorageService`, `BlobItemInfo`, `FileStorageModule`, `ExpeditionListArchiveModule`) already exist and are unchanged by this work.
