# Implementation Plan: Remove DownloadFromUrlAsync from IBlobStorageService (#4449)

Goal: `IBlobStorageService` becomes a pure blob-store contract; `DownloadFromUrlHandler` owns GET + upload; `FileStorageConstants` removed. Internals are visible to the test project (`InternalsVisibleTo Anela.Heblo.Tests`). Verification commands run from `backend/`.

### task: add-blob-naming-helper
Create `backend/src/Anela.Heblo.Application/Features/FileStorage/Infrastructure/BlobNaming.cs` (`internal static class BlobNaming`, namespace `Anela.Heblo.Application.Features.FileStorage.Infrastructure`) with:
- `GetExtensionFromContentType(string? mediaType)` and `GetContentTypeFromExtension(string fileName)`: copy the switch expressions verbatim from `AzureBlobStorageService` (lowercase the extension in the latter).
- `ResolveBlobName(string? requestedName, string fileUrl, string? responseMediaType)`: return `requestedName` if non-empty; else `Path.GetFileName(new Uri(fileUrl).LocalPath)`; if still empty return `$"downloaded-file-{Guid.NewGuid()}{GetExtensionFromContentType(responseMediaType)}"`.
Add `backend/test/Anela.Heblo.Tests/Features/FileStorage/BlobNamingTests.cs`: Theory covering every content-type->extension arm and default `.bin`; every extension->content-type arm incl. uppercase `.JPG` and default; ResolveBlobName for requested name, URL path name, and generated name (known/unknown media type). Write tests first, then implementation.
Verify: `dotnet test backend/test/Anela.Heblo.Tests --filter BlobNamingTests`.

### task: move-download-into-handler
Edit `DownloadFromUrlHandler.cs`: replace the `_blobStorageService.DownloadFromUrlAsync(...)` call inside the resilience delegate with a private `DownloadAndStoreAsync(request, ct)` as specified in `design.r1.md` (named client `FileStorageModule.FileDownloadClientName`, `GetAsync` with `ResponseHeadersRead`, `EnsureSuccessStatusCode`, stream, `BlobNaming`, `UploadAsync`, logging only the `UrlRedactor.Redact`ed URL, rethrow on error). Keep `using` scopes of response and stream across the awaited upload. Leave Handle's failure mapping, HEAD probe, and response shape unchanged.
Update `DownloadFromUrlHandlerTests.cs` (and `FileStorageValidationPipelineTests.cs`): remove every `Setup/Verify` of `DownloadFromUrlAsync`; add a helper that builds a mocked `IHttpClientFactory` returning an `HttpClient` over a stub `HttpMessageHandler` (HEAD and GET responses configurable, optionally throwing) and set `_blobStorage.UploadAsync` to return a blob URL. Port these scenarios from `AzureBlobStorageServiceTests`: named client resolved for GET; non-2xx -> failure response; cancellation token forwarded to GET/upload; blob name from URL path; UploadAsync receives response content type, else extension-inferred type; success and failure logs never contain the URL query secret (`token=secret`).
Verify: `dotnet test backend/test/Anela.Heblo.Tests --filter "FullyQualifiedName~FileStorage"` (will not compile until the interface task is done; do the interface task in the same working session before running).

### task: remove-interface-method-and-adapter-http
- `IBlobStorageService.cs`: delete `DownloadFromUrlAsync` and its summary.
- `AzureBlobStorageService.cs`: delete `DownloadFromUrlAsync`, `_httpClientFactory` field, ctor parameter, `GetExtensionFromContentType`, `GetContentTypeFromExtension`; ctor becomes `(BlobServiceClient, ILogger<AzureBlobStorageService>)`. No change needed in `AzureAdapterModule` (DI-resolved).
- `AzureBlobStorageServiceTests.cs`: delete all `DownloadFromUrlAsync_*` tests and the `_httpClientFactory` plumbing/related helpers; update the ~9 `new AzureBlobStorageService(...)` call sites to the two-arg ctor; keep all remaining tests.
- `MockBlobStorageService.cs` and `SimpleFileStorageTest.cs`: remove the `DownloadFromUrlAsync` member/usage (delete `SimpleFileStorageTest` test if it only exercises that method).
Verify: `grep -rn DownloadFromUrlAsync backend/` returns nothing; `dotnet build backend/Anela.Heblo.sln` succeeds.

### task: remove-constants-from-domain
- Delete `backend/src/Anela.Heblo.Domain/Features/FileStorage/FileStorageConstants.cs`.
- `FileStorageModule.cs`: `public const string FileDownloadClientName = "FileDownload";`; drop `using Anela.Heblo.Domain.Features.FileStorage;` if now unused.
- Fix any remaining `FileStorageConstants` references (tests: `AzureBlobStorageServiceTests` should be gone by then).
Verify: `grep -rn FileStorageConstants backend/` empty; `FileStorageModuleTests` (asserting `"FileDownload"`) pass.

### task: update-process-docs
- `docs/processes/module-file-storage.md`: rewrite step 3 of "How the download works" and the header chain to `DownloadFromUrlHandler` (GET + upload inside the retry delegate) -> `IBlobStorageService.UploadAsync`; remove references to `AzureBlobStorageService.DownloadFromUrlAsync`; adjust the "Code entry points" description for `IBlobStorageService.cs` (pure blob contract) and `AzureBlobStorageService.cs` (blob operations only, no HTTP); set `verified_at` to the current HEAD short SHA.
- `docs/processes/job-product-export-download.md`: change the `AzureBlobStorageService.cs` entry to "blob write (upload)" and the step 3 wording so the handler does HEAD, GET and upload; bump `verified_at`.
- Run `python3 scripts/process-docs/check.py index` (and `check.py` default check if available) and fix any complaints.
Verify: the process-docs check exits 0.

### task: final-validation
Run from repo root: `dotnet build backend/Anela.Heblo.sln`, `dotnet format backend/Anela.Heblo.sln --verify-no-changes` (run `dotnet format` if it reports changes), `dotnet test backend/test/Anela.Heblo.Tests --filter "FullyQualifiedName~FileStorage|FullyQualifiedName~ProductExport"`, then the full backend test project. Confirm greps: no `DownloadFromUrlAsync`, no `FileStorageConstants`, no `IHttpClientFactory` in `AzureBlobStorageService.cs`. No frontend or OpenAPI regeneration is required.
