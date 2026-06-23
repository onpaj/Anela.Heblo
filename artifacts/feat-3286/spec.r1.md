# Specification: Close Test Coverage Gap in AzureBlobStorageService

## Summary
`AzureBlobStorageService` (the Azure Blob Storage implementation of `IBlobStorageService`) sits at 39.5% line coverage, below the 60% CI filter threshold. The untested code is concentrated in the URL-download blob-name fallback chain, two private MIME/extension mapping switch expressions, the container-creation caching path, and the virtual-directory prefix-trimming logic. This specification defines the unit tests required to raise coverage above the threshold and lock in the behavior of these paths, without changing any production code.

## Background
`AzureBlobStorageService.DownloadFromUrlAsync` is exercised whenever a file is fetched from an external URL with no explicit blob name supplied — for example, when ingesting Shoptet product images. When the caller does not provide a `blobName`, the service derives one through a three-step fallback chain, and on the final step it must infer a file extension from the HTTP `Content-Type` header. The inverse mapping (extension → MIME type) is used when uploading a stream so the blob is served with the correct content type for browser display of images and PDFs.

Today these paths have no direct test coverage:
- The `blobName` generation fallback chain in `DownloadFromUrlAsync` (lines 45–56) is only partially exercised; the path where the URL has no filename and a GUID-based name is generated is reached by an existing test, but the existing `GetContentTypeFromExtension` "test" (`AzureBlobStorageServiceTests.GetContentTypeFromExtension_DifferentExtensions_ShouldReturnCorrectContentType`) contains only placeholder assertions and verifies nothing.
- The `GetExtensionFromContentType` switch (8 arms + `_ => .bin` fallback) and `GetContentTypeFromExtension` switch (13 arms + `_ => application/octet-stream` fallback) are private and not meaningfully tested.
- `GetOrCreateContainerAsync`'s `ConcurrentDictionary` cache "already-seen" branch is covered indirectly by `UploadAsync_CalledMultipleTimes_*`, but the URL-without-filename branch combinations are not.
- `ListVirtualDirectoriesAsync` (trailing-slash trimming of hierarchy prefixes) has no test at all.

A broken fallback would throw `UriFormatException` or store files under empty/malformed names; wrong content-type inference would break browser rendering of images and PDFs. These are subtle, silent failures, which is exactly why they need explicit regression tests.

Existing tests live in `backend/test/Anela.Heblo.Tests/Features/FileStorage/AzureBlobStorageServiceTests.cs` and already establish the mocking conventions (Moq over `BlobServiceClient`/`BlobContainerClient`/`BlobClient`, a `StubHttpMessageHandler`, and a mocked `IHttpClientFactory` returning the `FileDownloadClientName` named client). This work extends that file rather than introducing new infrastructure.

## Functional Requirements

### FR-1: Test blobName derived from URL path filename
When `DownloadFromUrlAsync` is called with `blobName = null` and the file URL contains a filename component in its path (e.g. `https://example.com/images/product.jpg`), the service must extract that filename (`product.jpg`) via `Path.GetFileName(uri.LocalPath)` and use it as the blob name.

**Acceptance criteria:**
- A test invokes `DownloadFromUrlAsync` with a URL ending in a real filename and `blobName` omitted.
- The mocked `BlobContainerClient.GetBlobClient` is verified to have been called with the exact filename extracted from the URL path (e.g. `product.jpg`), proving the filename-extraction branch (lines 47–48) executed.
- The test passes.

### FR-2: Test GUID-based blobName generation with content-type extension inference
When `DownloadFromUrlAsync` is called with `blobName = null` and the URL path has no filename component (e.g. `https://example.com/` or `https://example.com/download?id=5`), the service must generate a name of the form `downloaded-file-{GUID}{extension}`, where `{extension}` comes from `GetExtensionFromContentType` applied to the response's `Content-Type` media type.

**Acceptance criteria:**
- A test supplies a URL with no filename in the path and a response carrying `Content-Type: image/jpeg`; the blob name passed to `GetBlobClient` is verified to start with `downloaded-file-` and end with `.jpg`.
- A test supplies the same shape of URL with an unknown/unmapped `Content-Type` (e.g. `application/x-unknown` or a null content type); the blob name is verified to end with `.bin` (the `_ => ".bin"` fallback arm).
- Both tests pass.

### FR-3: Test GetExtensionFromContentType across all switch arms
Every arm of the `GetExtensionFromContentType` switch expression must be exercised through the public `DownloadFromUrlAsync` entry point (the method is private; test indirectly), including the default fallback.

**Acceptance criteria:**
- A `[Theory]` (or equivalent set of cases) drives `DownloadFromUrlAsync` with a no-filename URL and each of the following media types, asserting the resulting blob name suffix:
  - `image/jpeg` → `.jpg`
  - `image/png` → `.png`
  - `image/gif` → `.gif`
  - `image/webp` → `.webp`
  - `application/pdf` → `.pdf`
  - `text/plain` → `.txt`
  - `application/json` → `.json`
  - `application/xml` → `.xml`
  - an unmapped media type and a null media type → `.bin`
- The blob name asserted is the value captured from the `GetBlobClient(It.IsAny<string>())` callback or `It.Is<string>` match.
- All cases pass.

### FR-4: Test GetContentTypeFromExtension across all switch arms
Every arm of the `GetContentTypeFromExtension` switch must be exercised, including the default fallback. Since this method is private and is invoked from `DownloadFromUrlAsync` only when the response has no `Content-Type` header (line 59: `response.Content.Headers.ContentType?.MediaType ?? GetContentTypeFromExtension(blobName)`), the tests must drive it by supplying an explicit `blobName` (so the extension is known) together with a response that carries no `Content-Type` header, then assert the content type passed to the blob upload via `BlobUploadOptions.HttpHeaders.ContentType`.

**Acceptance criteria:**
- The existing placeholder test `GetContentTypeFromExtension_DifferentExtensions_ShouldReturnCorrectContentType` (lines 117–133), whose assertions verify nothing, is replaced with real tests.
- A `[Theory]` drives `DownloadFromUrlAsync` with an explicit `blobName` per extension and a response with **no** `Content-Type` header, asserting `BlobUploadOptions.HttpHeaders.ContentType` equals the expected MIME type for each of:
  - `.jpg` / `.jpeg` → `image/jpeg`
  - `.png` → `image/png`
  - `.gif` → `image/gif`
  - `.webp` → `image/webp`
  - `.pdf` → `application/pdf`
  - `.txt` → `text/plain`
  - `.json` → `application/json`
  - `.xml` → `application/xml`
  - `.zip` → `application/zip`
  - `.doc` → `application/msword`
  - `.docx` → `application/vnd.openxmlformats-officedocument.wordprocessingml.document`
  - `.xls` → `application/vnd.ms-excel`
  - `.xlsx` → `application/vnd.openxmlformats-officedocument.spreadsheetml.sheet`
  - an unknown extension (e.g. `.foo`) → `application/octet-stream`
- Case-insensitivity is covered: an uppercase extension (e.g. `FILE.PDF`) yields `application/pdf`, exercising the `ToLowerInvariant()` normalization (line 300).
- All cases pass.

### FR-5: Test GetOrCreateContainerAsync cache "already-seen" branch via the download path
The `ConcurrentDictionary`-backed cache in `GetOrCreateContainerAsync` must be shown to call `CreateIfNotExistsAsync` only once per container when reached through `DownloadFromUrlAsync` (not only through `UploadAsync`, which is already covered).

**Acceptance criteria:**
- A test calls `DownloadFromUrlAsync` twice for the same container on a single service instance.
- `BlobContainerClient.CreateIfNotExistsAsync` is verified to have been called exactly once (`Times.Once`), proving the `TryAdd` returns `false` on the second call and the create is skipped.
- The test passes.
- Note: the existing `UploadAsync_CalledMultipleTimes_*` tests already cover the upload path and must remain green; this requirement adds the download-path coverage and is satisfied if the existing coverage already exercises the branch — in that case the new test still serves as explicit regression protection.

### FR-6: Test ListVirtualDirectoriesAsync trailing-slash trimming
`ListVirtualDirectoriesAsync` enumerates blob hierarchy prefixes via `GetBlobsByHierarchyAsync(prefix: null, delimiter: "/")`, keeps only `IsPrefix` items, and strips a single trailing `/` from each prefix. This must be tested.

**Acceptance criteria:**
- A test mocks `BlobContainerClient.GetBlobsByHierarchyAsync` to return an async pageable containing a mix of prefix items (e.g. `2024/`, `archive/`) and non-prefix (blob) items.
- The returned list contains the prefixes with the trailing slash removed (`2024`, `archive`) and excludes all non-prefix items.
- A prefix without a trailing slash (if representable) is returned unchanged; an empty result set yields an empty list.
- The test passes.

### FR-7: No production code changes
The work is test-only. `AzureBlobStorageService.cs` and other production files must not be modified to make tests pass (no widening of access modifiers, no `[InternalsVisibleTo]` added solely for these tests if it can be avoided by testing through the public surface). All new tests drive behavior through the public `IBlobStorageService` methods.

**Acceptance criteria:**
- `git diff` shows changes only under `backend/test/`.
- All assertions reach private methods indirectly through public methods (`DownloadFromUrlAsync`, `UploadAsync`, `ListVirtualDirectoriesAsync`).

### FR-8: Coverage threshold met
The combined effect of FR-1 through FR-6 raises `AzureBlobStorageService` line coverage above the 60% filter threshold.

**Acceptance criteria:**
- After the new tests are added, the line coverage reported for `AzureBlobStorageService.cs` is ≥ 60%.
- The full `Anela.Heblo.Tests` suite passes (`dotnet test`), and `dotnet build` + `dotnet format` are clean.

## Non-Functional Requirements

### NFR-1: Performance
All tests are pure unit tests with mocked Azure SDK clients and a stub `HttpMessageHandler`; no network, disk, or live Azure access. Each test must complete in well under one second and add negligible time to the suite. No `Thread.Sleep`/real timers.

### NFR-2: Security
No secrets, connection strings, or live storage accounts are used. `BlobServiceClient` is mocked. No `UseDevelopmentStorage=true` emulator dependency. Tests must not read configuration or environment variables.

### NFR-3: Maintainability and consistency
New tests follow the existing conventions in `AzureBlobStorageServiceTests.cs`: Moq mocks for `BlobServiceClient`/`BlobContainerClient`/`BlobClient`, the existing nested `StubHttpMessageHandler` and `ThrowingHttpMessageHandler`, xUnit `[Fact]`/`[Theory]` with `[InlineData]`, and Arrange/Act/Assert comments. Prefer extending the existing test class over creating a new file. A shared private helper that wires up the container/blob mocks and captures the blob name passed to `GetBlobClient` is encouraged to reduce duplication.

### NFR-4: Determinism
Tests must be deterministic. The GUID in generated blob names must not be asserted literally; assert only the `downloaded-file-` prefix and the extension suffix (`StartsWith` / `EndsWith` or a regex). No reliance on ordering of unrelated tests; cache-behavior tests construct a fresh service instance so `_containerExists` starts empty.

## Data Model
No persistent data model. The unit under test, `AzureBlobStorageService`, depends on:
- `BlobServiceClient` → `BlobContainerClient` → `BlobClient` (Azure SDK; all mocked).
- `IHttpClientFactory` producing the named client `FileStorageModule.FileDownloadClientName` (`"FileDownload"`), backed by a stub handler.
- `ILogger<AzureBlobStorageService>` (mocked).

Relevant value shapes:
- Generated blob name: `downloaded-file-{Guid}{extension}` where `extension ∈ { .jpg, .png, .gif, .webp, .pdf, .txt, .json, .xml, .bin }`.
- Content type resolution: response `Content-Type` header takes precedence; falls back to `GetContentTypeFromExtension(blobName)` only when the header is absent.
- Virtual directory names: hierarchy prefix strings with a single trailing `/` removed.

## API / Interface Design
No public API changes. Tests exercise the existing `IBlobStorageService` surface:
- `Task<string> DownloadFromUrlAsync(string fileUrl, string containerName, string? blobName = null, CancellationToken)` — primary driver for FR-1 through FR-5.
- `Task<string> UploadAsync(Stream, string containerName, string blobName, string contentType, CancellationToken)` — used by existing cache tests; not the focus here.
- `Task<IReadOnlyList<string>> ListVirtualDirectoriesAsync(string containerName, CancellationToken)` — driver for FR-6.

Azure SDK mocking notes (the main complexity, per the brief):
- `BlobServiceClient`, `BlobContainerClient`, and `BlobClient` are mockable virtual-method classes; follow the existing setups in the test file.
- For FR-6, `GetBlobsByHierarchyAsync` returns `AsyncPageable<BlobHierarchyItem>`. Use `BlobsModelFactory.BlobHierarchyItem(prefix, blobItem)` to build prefix vs. blob items, and wrap them in an `AsyncPageable` (e.g. via `AsyncPageable<T>.FromPages` with a single `Page<T>` from `Page<T>.FromValues`, or a small async-enumerable helper). Confirm the exact factory/helper signatures against the installed `Azure.Storage.Blobs` version during implementation.
- To capture the generated blob name, set up `GetBlobClient(It.IsAny<string>())` with a `Callback<string>` that records the argument, or assert with `It.Is<string>(...)` / `Verify`.

## Dependencies
- Existing test stack: `xUnit`, `Moq`, `Azure.Storage.Blobs` (already referenced by `Anela.Heblo.Tests`).
- `Azure.Storage.Blobs.Models.BlobsModelFactory` for constructing `BlobHierarchyItem` / `BlobItem` test doubles.
- The named-client constant `FileStorageModule.FileDownloadClientName`.
- No new NuGet packages expected. If a test helper for `AsyncPageable` is needed, implement it inline within the test project rather than adding a dependency.

## Out of Scope
- Any change to `AzureBlobStorageService` production behavior, signatures, or access modifiers.
- Integration/E2E tests against real or emulated Azure storage.
- Refactoring `GetExtensionFromContentType` / `GetContentTypeFromExtension` into a shared/public mapping utility.
- Testing already-covered methods (`UploadAsync` happy path and exception propagation, `DeleteAsync`, `ExistsAsync`, `GetBlobUrl`, `ListBlobsAsync`, `DownloadAsync`, `BlobDownloadStream`) beyond what is incidentally re-exercised.
- Raising coverage of any file other than `AzureBlobStorageService.cs`.
- Performance/load testing of blob operations.

## Open Questions
None.

## Status: COMPLETE
