# Specification: Relocate AzureBlobStorageService to the Azure adapter layer

## Summary
`AzureBlobStorageService` — an I/O-bound Azure SDK consumer — currently lives in the Application layer, forcing `Anela.Heblo.Application` to take a compile-time dependency on the `Azure.Storage.Blobs` NuGet package and violating the project's Clean Architecture "I/O placement rule". This is an architecture-compliance refactor that moves the class (and its Azure-specific DI wiring) into `Anela.Heblo.Adapters.Azure`, mirroring the already-correct placement of `AzureBlobPrintQueueSink`. Behavior is unchanged; only the physical location and dependency graph change.

## Background
The daily arch-review routine (2026-07-04) flagged that `AzureBlobStorageService` at `backend/src/Anela.Heblo.Application/Features/FileStorage/Services/AzureBlobStorageService.cs` depends directly on `Azure.Storage.Blobs.BlobServiceClient` and performs network calls to Azure Blob Storage. Per `docs/architecture/filesystem.md`:

> **I/O placement rule**: Concrete `IPrintQueueSink` implementations and any I/O-bound service live in adapter projects under `backend/src/Adapters/`, not in `Features/{Feature}/Services/`.

The consequence of the current placement is that `Anela.Heblo.Application.csproj` references `Azure.Storage.Blobs` (line 12) and `FileStorageModule` reaches into the Azure SDK to register `BlobServiceClient`. The Application (inner) ring should depend only on abstractions; infrastructure libraries belong in the outer adapter ring. The codebase already has a working precedent: `AzureBlobPrintQueueSink` — a functionally equivalent Azure-SDK-backed I/O service — sits correctly in `backend/src/Adapters/Anela.Heblo.Adapters.Azure/Features/ExpeditionList/` with its DI wired via `AzureAdapterModule.AddAzurePrintQueueSink`. `AzureBlobStorageService` is the same shape of object and should follow the same pattern.

The domain abstraction `IBlobStorageService` and its DTO `BlobItemInfo` already live in `Anela.Heblo.Domain/Features/FileStorage/` and carry no Azure dependency. All eight `IBlobStorageService` consumers (the `ExpeditionListArchive` handlers and `DownloadFromUrlHandler`) depend only on that interface, so they are unaffected by moving the concrete implementation.

## Functional Requirements

### FR-1: Move the concrete service into the Azure adapter project
Relocate `AzureBlobStorageService` from the Application layer to the Azure adapter, alongside the existing `AzureBlobPrintQueueSink`, without changing its runtime behavior.

**Acceptance criteria:**
- The file exists at `backend/src/Adapters/Anela.Heblo.Adapters.Azure/Features/FileStorage/AzureBlobStorageService.cs` and no longer exists under `Anela.Heblo.Application/Features/FileStorage/Services/`.
- Its namespace is updated to sit under `Anela.Heblo.Adapters.Azure.Features.FileStorage` (consistent with `Anela.Heblo.Adapters.Azure.Features.ExpeditionList` used by the print queue sink).
- The class still implements `Anela.Heblo.Domain.Features.FileStorage.IBlobStorageService` and every public method retains identical logic and signatures (`DownloadFromUrlAsync`, `UploadAsync`, `DeleteAsync`, `GetBlobUrl`, `ExistsAsync`, `ListBlobsAsync`, `DownloadAsync`, `ListVirtualDirectoriesAsync`).
- The private `BlobDownloadStream` helper, the `_containerExists` cache, and the content-type helper methods move with the class unchanged.
- The reference to the named HTTP client constant (currently `FileStorageModule.FileDownloadClientName`) still resolves. It may remain a reference to the Application-layer constant (the adapter already references `Anela.Heblo.Application`) — no duplication of the string literal.

### FR-2: Move Azure-specific DI registration out of FileStorageModule
The `BlobServiceClient` factory registration and the `IBlobStorageService` → `AzureBlobStorageService` binding must move from `FileStorageModule` (Application layer) into a new extension method on `AzureAdapterModule` (adapter layer).

**Acceptance criteria:**
- A new extension method (e.g. `AddAzureBlobStorageService`) is added to `AzureAdapterModule` that:
  - Registers `BlobServiceClient` as a `Singleton` using the existing factory logic, including the Development-only `UseDevelopmentStorage=true` fallback with its warning log and the reliance on the already-validated `FileStorageOptions`.
  - Binds `services.AddSingleton<IBlobStorageService, AzureBlobStorageService>()`.
- `FileStorageModule` no longer references `BlobServiceClient`, no longer contains `using Azure.Storage.Blobs;`, and no longer binds `IBlobStorageService`.
- `FileStorageModule` retains all non-Azure registrations: the `FileStorageOptions` options binding and its non-Development `.Validate(...).ValidateOnStart()`, the named `FileDownload` `HttpClient` with its `SocketsHttpHandler`/timeout configuration, `IDownloadResilienceService` → `DownloadResilienceService`, and the `FileDownloadOptions` configuration binding.
- The `FileStorageOptions` type (and its `BlobConnectionString`) remains in the Application layer; the adapter reads it via `IOptions<FileStorageOptions>` (the adapter already references Application).

### FR-3: Wire the new registration into application startup
The new adapter extension must be invoked during startup so `IBlobStorageService` resolves exactly as before.

**Acceptance criteria:**
- The new `AddAzureBlobStorageService` extension is called during service registration (in `AzureAdapterModule` composition from the API startup path — e.g. `Anela.Heblo.API/Extensions/ServiceCollectionExtensions.cs` — or wherever `AddFileStorageModule` is composed, so the binding is always present in every environment that previously had it).
- `IBlobStorageService` continues to resolve to `AzureBlobStorageService` at runtime with no double-registration and no missing-service regression.
- The validation timing is preserved: `FileStorageOptions.ValidateOnStart()` still runs before any consumer resolves `BlobServiceClient` (i.e. the options validation registered by `FileStorageModule` is still in effect regardless of registration order).

### FR-4: Remove the Azure SDK dependency from the Application project
With FR-1 and FR-2 complete, the Application project no longer uses any Azure SDK type.

**Acceptance criteria:**
- The `<PackageReference Include="Azure.Storage.Blobs" ... />` line is removed from `Anela.Heblo.Application.csproj`.
- A repository-wide search confirms no remaining `using Azure.Storage` / `BlobServiceClient` / `BlobContainerClient` references inside `Anela.Heblo.Application`.
- `Anela.Heblo.Adapters.Azure.csproj` continues to reference `Azure.Storage.Blobs` (it already does, at version 12.25.0) — the dependency lives only in the outer ring.

### FR-5: Update tests to the new location
Existing tests that reference the concrete type or its module registration must compile and pass against the new structure.

**Acceptance criteria:**
- `backend/test/Anela.Heblo.Tests/Features/FileStorage/AzureBlobStorageServiceTests.cs` compiles against the relocated type (namespace/using updated). The test project already references `Anela.Heblo.Adapters.Azure`, so no new project reference is required.
- `backend/test/Anela.Heblo.Tests/Features/FileStorage/FileStorageModuleTests.cs` is updated so any assertions about `BlobServiceClient` / `IBlobStorageService` registration now target the new `AzureAdapterModule` extension rather than `FileStorageModule` (or are moved to a corresponding adapter-module test).
- All touched tests pass; no behavioral test assertions about blob operations change.

## Non-Functional Requirements

### NFR-1: Performance
No runtime performance change is intended. Singleton lifetimes are preserved for `BlobServiceClient`, `AzureBlobStorageService` (so the `_containerExists` cache survives across requests), and `IDownloadResilienceService`. HTTP client pooling (`PooledConnectionLifetime`, `AutomaticDecompression`) is unchanged.

### NFR-2: Security
No change to data sensitivity, authentication, or secret handling. The blob connection string continues to be sourced from configuration/Key Vault via `FileStorageOptions.BlobConnectionString`; the Development-only `UseDevelopmentStorage=true` fallback (with warning log) is preserved and remains unreachable in non-Development environments due to the fail-fast `ValidateOnStart()` guard.

### NFR-3: Architecture compliance
The change must satisfy the `filesystem.md` I/O placement rule and standard build/format validation: `dotnet build` succeeds, `dotnet format` reports clean, and the full backend test suite passes.

## Data Model
No data-model changes. Entities/DTOs involved (all unchanged and remaining in place):
- `IBlobStorageService` (Domain) — the abstraction consumed by handlers.
- `BlobItemInfo` (Domain) — `Name`, `FileName`, `CreatedOn`, `ContentLength`.
- `FileStorageOptions` (Application) — includes `BlobConnectionString`, section name `FileStorage`.
- `FileDownloadOptions` (Application) — bound from `FileStorage:Download`.

## API / Interface Design
No public HTTP API, contract, or UI change. The only interface-level changes are internal DI composition and C# namespaces:
- New: `AzureAdapterModule.AddAzureBlobStorageService(IServiceCollection, IConfiguration)` (registers `BlobServiceClient` + binds `IBlobStorageService`).
- Changed: `FileStorageModule.AddFileStorageModule(...)` loses its Azure registrations, keeps HTTP/options/resilience registrations.
- Changed: namespace of `AzureBlobStorageService` moves to `Anela.Heblo.Adapters.Azure.Features.FileStorage`.
- Unchanged: `FileStorageModule.FileDownloadClientName` remains the single source of the named-client string.

## Dependencies
- `Azure.Storage.Blobs` (12.25.0) — already referenced by `Anela.Heblo.Adapters.Azure`; being removed from `Anela.Heblo.Application`.
- `Anela.Heblo.Adapters.Azure` already has a project reference to `Anela.Heblo.Application`, giving the moved service access to `FileStorageOptions` and `FileStorageModule.FileDownloadClientName`.
- The API composition root (`Anela.Heblo.API`) already references the Azure adapter and calls `AddAzurePrintQueueSink`; it is the natural place to also invoke `AddAzureBlobStorageService`.

## Out of Scope
- Any change to blob operation behavior, error handling, logging messages, or method signatures.
- Refactoring `AzureBlobPrintQueueSink` or the `IPrintQueueSink` print-sink selection logic (`AddPrintQueueSink`).
- Consolidating or sharing a single `BlobServiceClient`/`BlobContainerClient` instance between the print queue sink and the blob storage service (they use different Azure SDK client types and different options; they remain independent).
- The `AzureBlobConflictTelemetryFilter` — it only references `AzureBlobStorageService` in a doc comment, so no code change is required (an optional comment touch-up is not in scope).
- Introducing any new configuration keys or Key Vault secrets.

## Open Questions
None. The following decisions were made as reasonable assumptions consistent with the existing precedent and the brief:
- The `BlobServiceClient` registration is **not** shared with `AddAzurePrintQueueSink` (which registers a `BlobContainerClient` from `PrintPickingListOptions`); the blob storage service keeps its own `BlobServiceClient` built from `FileStorageOptions`, matching current behavior.
- `AddAzureBlobStorageService` is invoked from the API composition root alongside the existing adapter wiring, ensuring the binding is present in all environments that had it before.
- `FileStorageModule.FileDownloadClientName` remains the canonical constant; the moved service references it rather than duplicating the literal.

## Status: COMPLETE
