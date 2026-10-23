# Architecture Review: Remove HTTP download from IBlobStorageService

## Skip Design: true
Backend-only refactor; no UI.

## Architectural Fit Assessment
Fits the Clean Architecture rules in `docs/architecture/development_guidelines.md`: Domain holds pure contracts, Application orchestrates use cases, Adapters implement external systems. Today the HTTP fetch is split: Application does HEAD, an Adapter does GET inside a Domain-declared method. Verified in code: the only production caller of `DownloadFromUrlAsync` is `DownloadFromUrlHandler`; the only implementer is `AzureBlobStorageService` (DI: `services.AddSingleton<IBlobStorageService, AzureBlobStorageService>()` in `AzureAdapterModule`, resolved by constructor injection, so dropping `IHttpClientFactory` needs no registration change). `FileStorageConstants` is used only by the module alias, the adapter and one test file. `ExpeditionListArchiveBlobStoreAdapter` and `AzureBlobPrintQueueSink` use only the pure members.

## Proposed Architecture

### Component Overview
```
DownloadFromUrlController -> MediatR -> DownloadFromUrlHandler (Application)
   HEAD probe (IHttpClientFactory, "FileDownload")
   IDownloadResilienceService.ExecuteWithResilienceAsync(ct =>
        GET (IHttpClientFactory) -> resolve name/content-type (BlobNaming helper)
        -> IBlobStorageService.UploadAsync(stream,...))   [Domain interface]
                                   |
                         AzureBlobStorageService (Adapter; Azure SDK only)
```

### Key Design Decisions

#### Decision 1: Logic lives in the handler, mappings in an internal static helper
**Options considered:** (a) inline everything in handler; (b) new `IFileDownloader` abstraction; (c) handler + internal static `BlobNaming` helper.
**Chosen approach:** (c). New `internal static class BlobNaming` in `Application/Features/FileStorage/Infrastructure/` with `ResolveBlobName(...)`/`GetExtensionFromContentType` and `GetContentTypeFromExtension`, moved verbatim from the adapter. Tests project must be able to see it: check `InternalsVisibleTo` in Application; if absent, make the class `public static`.
**Rationale:** The issue explicitly says additive to the handler; (b) is over-engineering. A static helper keeps the handler readable and the mapping arms unit-testable.

#### Decision 2: Keep retry boundary = GET + upload
**Chosen approach:** Whole GET+upload sits inside the resilience delegate, exactly mirroring today's boundary (adapter did both inside the delegate). Do not move upload outside.
**Rationale:** No behaviour change in retry/telemetry semantics; the stream is per-attempt.

#### Decision 3: Client name constant
**Chosen approach:** `FileStorageModule.FileDownloadClientName = "FileDownload"` literal; delete `FileStorageConstants.cs` (only member). Value must not change (telemetry `Job` name, existing test).

#### Decision 4: UrlRedactor stays in Domain
Out of scope; moving it would widen the change. Note as follow-up only.

## Implementation Guidance

### Directory / Module Structure
- Edit `Domain/Features/FileStorage/IBlobStorageService.cs` (remove method).
- Delete `Domain/Features/FileStorage/FileStorageConstants.cs`.
- Edit `Adapters.Azure/Features/FileStorage/AzureBlobStorageService.cs` (remove method, field, ctor arg, two helpers; remove unused usings).
- Add `Application/Features/FileStorage/Infrastructure/BlobNaming.cs`.
- Edit `Application/.../DownloadFromUrlHandler.cs` and `FileStorageModule.cs`.
- Tests under `backend/test/Anela.Heblo.Tests/Features/FileStorage/` (see plan).
- Docs: `docs/processes/module-file-storage.md`, `docs/processes/job-product-export-download.md`.

### Interfaces and Contracts
`IBlobStorageService` minus `DownloadFromUrlAsync`. `AzureBlobStorageService(BlobServiceClient, ILogger<AzureBlobStorageService>)`. Handler ctor unchanged (already has `IBlobStorageService`, `IHttpClientFactory`).

### Data Flow
Request -> validation behavior -> handler: HEAD (size) -> resilience { GET headers-only, EnsureSuccess, stream, name, content type, UploadAsync } -> response with `BlobUrl`, `BlobName` (request name or derived from returned blob URL), `FileSizeBytes` from HEAD. Failure mapping unchanged.

## Risks and Mitigations
| Risk | Severity | Mitigation |
|------|----------|------------|
| Behaviour drift in naming/content-type mappings | Medium | Move code verbatim; port all existing adapter tests (all content-type arms, uppercase ext) to helper/handler tests |
| Response/stream disposed before upload finishes | Medium | Keep `using var response` and `using var stream` scoped across the awaited `UploadAsync` |
| Query secret leakage in new handler logs | Medium | Log only `UrlRedactor.Redact` output; keep the two secret-logging tests, ported |
| Large test churn (DownloadFromUrlHandlerTests mocks removed method ~10 places) | Low | Introduce a test helper that builds a stub `HttpMessageHandler` + factory |
| Process-docs CI gate fails | Medium | Update both docs and bump `verified_at` in the same PR; run `check.py index` |
| Test project cannot see internal helper | Low | Check `InternalsVisibleTo`; otherwise make it public |

## Specification Amendments
None beyond: confirm `InternalsVisibleTo` (Decision 1) and that `AzureBlobStorageServiceTests` ctor usages (many inline `new AzureBlobStorageService(...)`) are all updated.

## Prerequisites
None (no migrations, config or infra).
