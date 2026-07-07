# Design: Relocate AzureBlobStorageService to the Azure adapter layer

## Component Design

### `AzureBlobStorageService` (relocated, unchanged behavior)
- **Old location:** `Anela.Heblo.Application/Features/FileStorage/Services/AzureBlobStorageService.cs`, namespace `Anela.Heblo.Application.Features.FileStorage.Services`.
- **New location:** `Anela.Heblo.Adapters.Azure/Features/FileStorage/AzureBlobStorageService.cs`, namespace `Anela.Heblo.Adapters.Azure.Features.FileStorage`.
- **Responsibility:** unchanged — the sole concrete, I/O-bound implementation of `IBlobStorageService`, backed by the Azure `BlobServiceClient` and `IHttpClientFactory` (named `FileDownload` client).
- **Contract:** implements `Anela.Heblo.Domain.Features.FileStorage.IBlobStorageService` with identical method signatures: `DownloadFromUrlAsync`, `UploadAsync`, `DeleteAsync`, `GetBlobUrl`, `ExistsAsync`, `ListBlobsAsync`, `DownloadAsync`, `ListVirtualDirectoriesAsync`.
- **Internals moved verbatim:** the private `BlobDownloadStream` helper, the `_containerExists` cache, and the content-type helper methods — no logic changes.
- **Cross-ring reference:** keeps a `using Anela.Heblo.Application.Features.FileStorage;` to reach `FileStorageModule.FileDownloadClientName` (the adapter already references `Anela.Heblo.Application`, so this is a normal outer-ring→middle-ring reference, not a cycle).
- **Constructor dependencies:** `BlobServiceClient`, `IHttpClientFactory`, `ILogger<AzureBlobStorageService>` — unchanged.

### `AzureAdapterModule.AddAzureBlobStorageService` (new extension)
- **Location:** `Anela.Heblo.Adapters.Azure/AzureAdapterModule.cs`, alongside the existing `AddAzurePrintQueueSink`.
- **Signature:** `public static IServiceCollection AddAzureBlobStorageService(this IServiceCollection services, IHostEnvironment environment)`.
- **Responsibility:**
  1. Registers `BlobServiceClient` as a `Singleton`, using the factory moved verbatim from `FileStorageModule`: reads `IOptions<FileStorageOptions>`, and if `BlobConnectionString` is blank, logs a warning interpolating `environment.EnvironmentName` and falls back to `new BlobServiceClient("UseDevelopmentStorage=true")`; otherwise constructs `new BlobServiceClient(opts.BlobConnectionString)`.
  2. Binds `services.AddSingleton<IBlobStorageService, AzureBlobStorageService>()`.
- **Independence from `AddAzurePrintQueueSink`:** a separate extension, not folded together and not sharing a `BlobServiceClient`/`BlobContainerClient` — different SDK client types, different options sources (`FileStorageOptions` vs `PrintPickingListOptions`), different lifecycles.
- **Composition:** invoked unconditionally from the API composition root (`Program.cs`, `builder.Services.AddAzureBlobStorageService(builder.Environment);`, immediately after `AddApplicationServices`) — not from `ApplicationModule` (would create an Application→Adapter cycle) and not from inside `AddPrintQueueSink`'s conditional `switch` (would make the binding conditional on `ExpeditionList:PrintSink`, regressing `FileSystem`/`Cups` environments).

### `FileStorageModule` (reduced responsibility)
- **Loses:** the `BlobServiceClient` factory, the `AddSingleton<IBlobStorageService, AzureBlobStorageService>()` binding, `using Azure.Storage.Blobs;`, and the now-unused `using ...Features.FileStorage.Services;`.
- **Keeps:** `FileStorageOptions` binding with the non-Development `.Validate(...).ValidateOnStart()`, the named `FileDownload` `HttpClient` (`SocketsHttpHandler`, timeout config), `IDownloadResilienceService` → `DownloadResilienceService`, `FileDownloadOptions` binding, and the `FileDownloadClientName` constant (remains the single source of truth referenced by the relocated service).
- **Ordering guarantee:** `FileStorageModule` always runs during `AddApplicationServices`, so `IOptions<FileStorageOptions>` is bound and validated before `AddAzureBlobStorageService`'s factory is ever invoked (first resolve happens lazily, after all `Add*` calls complete) — registration order between the two modules is not significant.

## Data Schemas

No schema or contract changes; all shapes below are unchanged and listed only to confirm they are unaffected by the move.

- **`IBlobStorageService`** (`Anela.Heblo.Domain/Features/FileStorage/`) — abstraction consumed by `DownloadFromUrlHandler` and the `ExpeditionListArchive` handlers. Method signatures unchanged.
- **`BlobItemInfo`** (`Anela.Heblo.Domain/Features/FileStorage/`) — DTO fields unchanged: `Name`, `FileName`, `CreatedOn`, `ContentLength`.
- **`FileStorageOptions`** (`Anela.Heblo.Application`) — unchanged; section name `FileStorage`; includes `BlobConnectionString`, now read by the adapter via `IOptions<FileStorageOptions>` instead of by `FileStorageModule` directly constructing `BlobServiceClient`.
- **`FileDownloadOptions`** (`Anela.Heblo.Application`) — unchanged; bound from `FileStorage:Download`.
- **DI surface (internal, C#-only, no HTTP/API shape change):**
  - New: `AzureAdapterModule.AddAzureBlobStorageService(IServiceCollection, IHostEnvironment)`.
  - Changed: `FileStorageModule.AddFileStorageModule(...)` no longer registers `BlobServiceClient` or binds `IBlobStorageService`.
  - Changed: `AzureBlobStorageService` namespace → `Anela.Heblo.Adapters.Azure.Features.FileStorage`.
  - Unchanged: `FileStorageModule.FileDownloadClientName` = `"FileDownload"`.

No public HTTP API, request/response payload, or event schema is introduced, removed, or modified by this refactor.
