# Design: Remove Azure Adapter's Compile-Time Dependency on the Application Layer (FileStorage)

## Component Design

- **New: `FileStorageConstants`** (`Anela.Heblo.Domain.Features.FileStorage`) — a `public static class` holding `public const string FileDownloadClientName = "FileDownload"`. Sole responsibility: own the logical `HttpClient` name as a Domain-layer compile-time constant, following the `PurchaseOrderConstants` precedent.
- **Changed: `AzureBlobStorageService`** (`Anela.Heblo.Adapters.Azure`) — drops its `using Anela.Heblo.Application.Features.FileStorage;` import and resolves the named `HttpClient` via `FileStorageConstants.FileDownloadClientName` instead of `FileStorageModule.FileDownloadClientName`. No other behavior changes.
- **Changed: `FileStorageModule`** (`Anela.Heblo.Application.Features.FileStorage`) — `FileDownloadClientName` becomes a forwarding const (`= FileStorageConstants.FileDownloadClientName`), preserving the existing public member for `DownloadFromUrlHandler.cs` and tests with zero call-site changes.
- **Changed: `AzureBlobStorageServiceTests`** — swaps its `FileStorageModule.FileDownloadClientName` references (~11 sites) for `FileStorageConstants.FileDownloadClientName` and updates the `using` directive accordingly.
- **Unchanged:** `AzureAdapterModule.cs`, `IBlobStorageService`, `DownloadFromUrlHandler.cs`, `.csproj` references, DI registration, and all HttpClient behavior (pooling, timeout, decompression).

## Data Schemas

Not applicable — no persisted data, API contract, or event payload changes. This is a compile-time constant relocation with an identical runtime string value (`"FileDownload"`).
