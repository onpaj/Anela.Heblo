## Module
FileStorage

## Finding
`AzureBlobStorageService` lives in the Application layer at:

```
backend/src/Anela.Heblo.Application/Features/FileStorage/Services/AzureBlobStorageService.cs
```

It is an I/O-bound Azure SDK consumer — it depends directly on `Azure.Storage.Blobs.BlobServiceClient` and makes network calls to Azure Blob Storage. That makes it adapter-tier code, not Application-tier code.

As a direct consequence, `FileStorageModule` also reaches into Azure-SDK territory:
- **line 5**: `using Azure.Storage.Blobs;`
- **lines 42–57**: registers `BlobServiceClient` as a `Singleton` via an SDK factory
- **line 84**: `services.AddSingleton()`

The codebase already demonstrates the correct approach: `AzureBlobPrintQueueSink` (functionally equivalent — an Azure-SDK-backed I/O service) is correctly placed in:

```
backend/src/Adapters/Anela.Heblo.Adapters.Azure/Features/ExpeditionList/AzureBlobPrintQueueSink.cs
```

with its `BlobContainerClient` registration and DI binding in `AzureAdapterModule.AddAzurePrintQueueSink`. `AzureBlobStorageService` is the same shape of object but skipped that placement.

## Why it matters
`filesystem.md` states the rule explicitly:

> **I/O placement rule**: Concrete `IPrintQueueSink` implementations and any I/O-bound service live in adapter projects under `backend/src/Adapters/`, not in `Features/{Feature}/Services/`.

With the current placement `Anela.Heblo.Application.csproj` takes a compile-time dependency on the `Azure.Storage.Blobs` NuGet package. Clean Architecture requires the Application layer to depend only on abstractions — infrastructure libraries belong in the outer ring. It also creates an inconsistency with `AzureBlobPrintQueueSink`, making the codebase harder to reason about: one Azure-blob service is in the correct place, the other is not.

## Suggested fix
1. Move `AzureBlobStorageService` to `backend/src/Adapters/Anela.Heblo.Adapters.Azure/Features/FileStorage/AzureBlobStorageService.cs`.
2. Add a new extension method (e.g. `AddAzureBlobStorageService`) to `AzureAdapterModule`, which registers `BlobServiceClient` (or reuse the one already registered by `AddAzurePrintQueueSink`) and binds `services.AddSingleton()`.
3. Remove the `BlobServiceClient` factory and `IBlobStorageService` binding from `FileStorageModule` — keep only the `HttpClient`, `FileStorageOptions`, `FileDownloadOptions`, and `IDownloadResilienceService` registrations (none of which require the Azure SDK).
4. Remove `using Azure.Storage.Blobs;` from `FileStorageModule`.
5. In `ApplicationModule` (or `Program.cs`), call the new `AzureAdapterModule` extension alongside the existing `AddAzurePrintQueueSink`.

`IBlobStorageService`, `BlobItemInfo`, `FileStorageOptions`, `FileDownloadOptions`, `IDownloadResilienceService`, and `DownloadFromUrlHandler` all stay where they are — none of them carry an Azure SDK dependency.

---
_Filed by daily arch-review routine on 2026-07-04._
