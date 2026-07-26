## Module
FileStorage

## Finding
`AzureBlobStorageService` (in `backend/src/Adapters/Anela.Heblo.Adapters.Azure/Features/FileStorage/AzureBlobStorageService.cs`, line 39) imports and uses `FileStorageModule.FileDownloadClientName` from `Anela.Heblo.Application.Features.FileStorage`:

```csharp
using Anela.Heblo.Application.Features.FileStorage;
// ...
var httpClient = _httpClientFactory.CreateClient(FileStorageModule.FileDownloadClientName);
```

`FileStorageModule` is an Application-layer class. This means the Azure adapter (infrastructure/outer ring) has a compile-time dependency on the Application layer.

## Why it matters
Clean Architecture's dependency rule requires outer rings to depend inward only — adapters depend on Domain, not Application. Here the adapter crosses that boundary just to access a string constant (`"FileDownload"`). This introduces an unwanted coupling: changing or splitting `FileStorageModule` can now break the Azure adapter. It also makes the adapter harder to test in isolation (the Application project must be referenced).

## Suggested fix
Move the constant to the Domain layer (e.g. as a `public const` on `IBlobStorageService` or a new `FileStorageConstants` class in `Anela.Heblo.Domain.Features.FileStorage`), or introduce an options class (e.g. `BlobStorageAdapterOptions`) injected into the adapter that carries the client name. Either approach removes the adapter's dependency on the Application project.

---
_Filed by daily arch-review routine on 2026-07-17._
