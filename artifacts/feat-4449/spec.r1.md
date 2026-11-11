# Specification: Remove HTTP download from the FileStorage Domain contract (IBlobStorageService)

## Summary
`IBlobStorageService` (Domain) exposes `DownloadFromUrlAsync(fileUrl, ...)`, which forces the Azure adapter to perform HTTP fetching via `IHttpClientFactory` while the Application-layer `DownloadFromUrlHandler` also performs HTTP (HEAD probe) and owns the retry pipeline. This refactor makes `IBlobStorageService` a pure blob-store contract and moves the whole "GET url -> upload blob" flow into `DownloadFromUrlHandler`. Behaviour visible to callers (endpoint, `ProductExportDownloadJob`) must not change.

## Background
Issue #4449 (arch-review finding, module FileStorage). Current state:
- `Domain/Features/FileStorage/IBlobStorageService.cs` declares `DownloadFromUrlAsync`.
- `Adapters.Azure/.../AzureBlobStorageService.cs` takes `IHttpClientFactory`, does GET, derives blob name / content type (`GetExtensionFromContentType`, `GetContentTypeFromExtension`), then calls its own `UploadAsync`.
- `Application/.../DownloadFromUrlHandler.cs` does a HEAD probe through `IHttpClientFactory` and wraps `IBlobStorageService.DownloadFromUrlAsync` in `IDownloadResilienceService.ExecuteWithResilienceAsync`.
- `Domain/.../FileStorageConstants.FileDownloadClientName = "FileDownload"` is aliased by `FileStorageModule.FileDownloadClientName` and used by the adapter.
- Process doc `docs/processes/module-file-storage.md` (and `job-product-export-download.md`) describe the current flow and list `AzureBlobStorageService` as the GET implementation.

## Functional Requirements

### FR-1: Remove DownloadFromUrlAsync from IBlobStorageService
Delete the method (and its doc comment) from the Domain interface. Remaining members unchanged: `UploadAsync`, `DeleteAsync`, `GetBlobUrl`, `ExistsAsync`, `ListBlobsAsync`, `DownloadAsync`, `ListVirtualDirectoriesAsync`.
**Acceptance criteria:**
- `grep -rn DownloadFromUrlAsync backend/` returns no matches (src or tests).
- Solution builds.

### FR-2: AzureBlobStorageService no longer performs HTTP
Remove the `DownloadFromUrlAsync` implementation, the `IHttpClientFactory` constructor parameter/field, and the now-unused private helpers `GetExtensionFromContentType` and `GetContentTypeFromExtension` (they move, see FR-3). Constructor becomes `(BlobServiceClient, ILogger<AzureBlobStorageService>)`.
**Acceptance criteria:**
- Adapter has no `System.Net.Http` / `IHttpClientFactory` reference in this class.
- DI registration in `AzureAdapterModule` still resolves (verify it does not construct the service manually with the factory).
- `AzureBlobStorageService` upload/delete/list/etc. behaviour is unchanged.

### FR-3: DownloadFromUrlHandler owns the full download-and-store flow
Inside the delegate passed to `IDownloadResilienceService.ExecuteWithResilienceAsync`, the handler, per attempt:
1. creates the named client via `_httpClientFactory.CreateClient(FileStorageModule.FileDownloadClientName)`;
2. `GetAsync(fileUrl, HttpCompletionOption.ResponseHeadersRead, ct)` and `EnsureSuccessStatusCode()`;
3. reads the content stream;
4. resolves blob name: `request.BlobName`, else `Path.GetFileName(new Uri(fileUrl).LocalPath)`, else `downloaded-file-{Guid}{ext}` with ext from response media type (jpeg->.jpg, png, gif, webp, pdf->.pdf, text/plain->.txt, json, xml, default .bin);
5. resolves content type: response `Content-Type` media type, else inferred from blob-name extension, case-insensitive (jpg/jpeg, png, gif, webp, pdf, txt, json, xml, zip, doc, docx, xls, xlsx, default `application/octet-stream`);
6. calls `IBlobStorageService.UploadAsync(stream, containerName, blobName, contentType, ct)` and returns the blob URL.
The mappings are moved verbatim (no behavioural change) into the Application layer, preferably as a small internal static helper in `Features/FileStorage/Infrastructure/` so they are unit-testable.
Logging previously done in the adapter ("Starting download", success, error with redacted URL) is preserved in the handler using `UrlRedactor.Redact`; query strings must never be logged.
**Acceptance criteria:**
- Response contract (`DownloadFromUrlResponse` fields, `ErrorCode`, `Params` incl. `cause`, `attemptCount`) is unchanged for success, timeout, HTTP error, and retry-exhausted paths.
- HEAD probe behaviour unchanged (best-effort, `HeadTimeout`).
- HTTP and upload both occur inside the resilience delegate so retry boundaries are identical to today.
- No test or log output contains URL query secrets.

### FR-4: Move FileDownloadClientName out of Domain
Delete `Domain/Features/FileStorage/FileStorageConstants.cs`. `FileStorageModule.FileDownloadClientName` becomes the literal `"FileDownload"` (value unchanged; it is also the telemetry `Job` name in the resilience service and an existing test asserts the string).
**Acceptance criteria:**
- No `FileStorageConstants` references remain.
- `FileStorageModuleTests` asserting `"FileDownload"` still pass.

### FR-5: Tests updated
- `DownloadFromUrlHandlerTests`, `FileStorageValidationPipelineTests`: replace mocks of `DownloadFromUrlAsync` with an `HttpMessageHandler`-backed client (via mocked `IHttpClientFactory`) plus `Mock<IBlobStorageService>.UploadAsync` verification.
- `AzureBlobStorageServiceTests`: delete the `DownloadFromUrlAsync_*` tests and the factory plumbing; keep all other tests, updating the constructor.
- Port the behaviour-bearing download tests to handler/helper tests: named client resolved, non-2xx -> failure, cancellation forwarded, blob name from URL path, blob name generated with known/unknown content-type extension (all arms), content-type inference incl. uppercase extension, container-creation not relevant here, success and failure do not log URL query secret.
- `MockBlobStorageService`, `SimpleFileStorageTest`: drop the removed method.
**Acceptance criteria:** `dotnet test` for FileStorage-related tests passes; no coverage regression of the moved logic.

### FR-6: Process docs updated in the same PR
Per CLAUDE.md, update `docs/processes/module-file-storage.md` (How the download works: now `DownloadFromUrlHandler` -> `DownloadResilienceService` with GET + upload in the handler; Code entry points; External systems wording; bump `verified_at`) and `docs/processes/job-product-export-download.md` (code entry point line for `AzureBlobStorageService` becomes upload only; bump `verified_at`). Run `python3 scripts/process-docs/check.py index`.
**Acceptance criteria:** the process-docs check passes locally.

## Non-Functional Requirements

### NFR-1: Performance
Streaming preserved (`ResponseHeadersRead`, stream passed straight to `UploadAsync`); no buffering of the body in memory.

### NFR-2: Security
URL query strings (tokens) must not appear in logs or failure params; keep `UrlRedactor` usage. No new secrets/config.

### NFR-3: Architecture
Domain contains no HTTP/infrastructure names. Dependency direction Domain <- Application <- Adapters preserved. DTOs remain classes; no OpenAPI change expected (verify no controller/DTO touched).

## Data Model
No change. No migrations.

## API / Interface Design
- HTTP API `POST /api/FileStorage/download` unchanged.
- `IBlobStorageService`: one method removed (breaking only for in-repo implementers/mocks).
- `AzureBlobStorageService` ctor: drops `IHttpClientFactory`.
- `FileStorageModule.FileDownloadClientName` stays public, now a literal.

## Dependencies
`IHttpClientFactory` (already injected in handler), `IDownloadResilienceService`, `UrlRedactor` (Domain; stays), named HttpClient registration in `FileStorageModule` (unchanged).

## Out of Scope
- Moving `UrlRedactor` out of Domain.
- Changing retry policy (e.g. not retrying 4xx, a documented quirk), timeouts, or the HEAD probe.
- Splitting HTTP fetching into a separate abstraction (e.g. `IFileDownloader`).
- Any frontend/OpenAPI changes.

## Open Questions
None.

## Status: COMPLETE
