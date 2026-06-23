# Design: Close Test Coverage Gap in AzureBlobStorageService

## Component Design

### Test class: AzureBlobStorageServiceTests (extended)

**File:** `backend/test/Anela.Heblo.Tests/Features/FileStorage/AzureBlobStorageServiceTests.cs`

The existing class is extended in-place. No new files are introduced. The class already owns the shared constructor-level mocks (`_mockBlobServiceClient`, `_mockLogger`, `_mockHttpClientFactory`, `_stubHandler`, `_service`). New tests follow the same structure.

#### Shared private helpers (new)

Two private helper methods are added to the class to reduce duplication across the new test groups:

**`SetupContainerAndBlobClient(string containerName, out Mock<BlobContainerClient> containerMock, out Mock<BlobClient> blobMock, out List<string> capturedBlobNames)`**
- Wires `_mockBlobServiceClient.GetBlobContainerClient(containerName)` to a fresh `Mock<BlobContainerClient>`.
- Wires `containerMock.GetBlobClient(It.IsAny<string>())` with a `Callback<string>` that appends to `capturedBlobNames`, returning `blobMock.Object`.
- Sets up `containerMock.CreateIfNotExistsAsync(...)` to return a successful response.
- Sets up `blobMock.Uri` with a synthetic but valid `Uri` and `blobMock.UploadAsync(...)` to return successfully.

This helper is used by FR-1, FR-2, FR-3, FR-4, and FR-5 tests so blob-name capture and container wiring are not repeated inline.

**`BuildNoContentTypeHandler(string responseBody = "data")`**
- Returns a `StubHttpMessageHandler` whose response carries no `Content-Type` header (achieved by creating a `ByteArrayContent` without setting `ContentType`).
- Used by FR-4 tests that must drive the `GetContentTypeFromExtension` fallback (triggered when the response header is absent).

#### Async pageable helper (new, for FR-6)

A private static method `CreateAsyncPageable<T>(IEnumerable<T> items)` wraps a synchronous list into a single-page `AsyncPageable<T>` using `Page<T>.FromValues` + `AsyncPageable<T>.FromPages`. This is self-contained within the test file and introduces no new dependency.

#### New test groups

**FR-1 — blobName from URL path**
- `[Fact] DownloadFromUrlAsync_UrlWithFilename_UsesBlobNameFromPath`
  - Input URL: `https://example.com/images/product.jpg`; `blobName` omitted.
  - Asserts `GetBlobClient` was called with `"product.jpg"` via `capturedBlobNames`.

**FR-2 — GUID-based blobName with content-type extension**
- `[Fact] DownloadFromUrlAsync_UrlWithNoFilename_KnownContentType_UsesPrefixAndExtension`
  - Input URL: `https://example.com/download?id=5`; response `Content-Type: image/jpeg`.
  - Asserts captured blob name starts with `"downloaded-file-"` and ends with `".jpg"`.
- `[Fact] DownloadFromUrlAsync_UrlWithNoFilename_UnknownContentType_UsesBinExtension`
  - Input URL same shape; response `Content-Type: application/x-unknown`.
  - Asserts captured blob name ends with `".bin"`.

**FR-3 — GetExtensionFromContentType all arms**
- `[Theory] DownloadFromUrlAsync_NoFilenameUrl_ContentTypeToExtension_AllArms`
  - `[InlineData]` rows for: `image/jpeg` → `.jpg`, `image/png` → `.png`, `image/gif` → `.gif`, `image/webp` → `.webp`, `application/pdf` → `.pdf`, `text/plain` → `.txt`, `application/json` → `.json`, `application/xml` → `.xml`, unmapped type → `.bin`, null content type → `.bin`.
  - Uses `SetupContainerAndBlobClient`; asserts `capturedBlobNames[0].EndsWith(expectedExtension)`.

**FR-4 — GetContentTypeFromExtension all arms (replaces placeholder test)**
- The existing placeholder test `GetContentTypeFromExtension_DifferentExtensions_ShouldReturnCorrectContentType` is deleted.
- Replaced by:
  - `[Theory] DownloadFromUrlAsync_WithExplicitBlobName_NoResponseContentType_UsesExtensionMapping`
    - `[InlineData]` rows for: `.jpg`/`.jpeg` → `image/jpeg`, `.png` → `image/png`, `.gif` → `image/gif`, `.webp` → `image/webp`, `.pdf` → `application/pdf`, `.txt` → `text/plain`, `.json` → `application/json`, `.xml` → `application/xml`, `.zip` → `application/zip`, `.doc` → `application/msword`, `.docx` → `application/vnd.openxmlformats-officedocument.wordprocessingml.document`, `.xls` → `application/vnd.ms-excel`, `.xlsx` → `application/vnd.openxmlformats-officedocument.spreadsheetml.sheet`, `.foo` → `application/octet-stream`.
    - For each row: calls `DownloadFromUrlAsync` with an explicit `blobName` containing the extension, a response with no `Content-Type` header (from `BuildNoContentTypeHandler`), and verifies `blobMock.UploadAsync` was called with `BlobUploadOptions.HttpHeaders.ContentType == expectedMimeType`.
  - `[Fact] DownloadFromUrlAsync_WithUppercaseBlobExtension_NormalizesContentType`
    - Explicit `blobName = "FILE.PDF"`, no response content type; verifies upload options carry `"application/pdf"`.

**FR-5 — Container cache via DownloadFromUrlAsync**
- `[Fact] DownloadFromUrlAsync_CalledTwice_SameContainer_CallsCreateIfNotExistsOnce`
  - Constructs a fresh `AzureBlobStorageService` instance (not `_service`) so the cache starts empty.
  - Calls `DownloadFromUrlAsync` twice for the same container with different URLs both carrying an explicit `blobName`.
  - Verifies `CreateIfNotExistsAsync` called `Times.Once`.

**FR-6 — ListVirtualDirectoriesAsync trailing-slash trimming**
- `[Fact] ListVirtualDirectoriesAsync_TrimsTrailingSlash_FromPrefixes`
  - Builds `BlobHierarchyItem` instances via `BlobsModelFactory.BlobHierarchyItem`: two prefix items (`"2024/"`, `"archive/"`) and one blob item.
  - Mocks `containerClient.GetBlobsByHierarchyAsync(delimiter: "/", prefix: null, ...)` via the async pageable helper.
  - Asserts result equals `["2024", "archive"]` (prefix-only, slash stripped).
- `[Fact] ListVirtualDirectoriesAsync_EmptyContainer_ReturnsEmptyList`
  - Same setup with an empty page; asserts empty list returned.

### Stub handlers (existing, unchanged)

- `StubHttpMessageHandler` — returns configurable status code and body; respects `CancellationToken`.
- `ThrowingHttpMessageHandler` — throws a supplied exception unconditionally.

Both remain as private nested classes in the test file.

## Data Schemas

This feature introduces no persistent data, no database schema changes, and no API changes. The test infrastructure operates exclusively against mocked collaborators.

### Value shapes under test

| Concept | Shape | Source |
|---|---|---|
| Generated blob name (GUID path) | `downloaded-file-{Guid:D}{extension}` | `DownloadFromUrlAsync` internal logic |
| Extension-from-content-type map | `image/jpeg`→`.jpg`, `image/png`→`.png`, `image/gif`→`.gif`, `image/webp`→`.webp`, `application/pdf`→`.pdf`, `text/plain`→`.txt`, `application/json`→`.json`, `application/xml`→`.xml`, `*`→`.bin` | `GetExtensionFromContentType` (private) |
| Content-type-from-extension map | `.jpg`/`.jpeg`→`image/jpeg`, `.png`→`image/png`, `.gif`→`image/gif`, `.webp`→`image/webp`, `.pdf`→`application/pdf`, `.txt`→`text/plain`, `.json`→`application/json`, `.xml`→`application/xml`, `.zip`→`application/zip`, `.doc`→`application/msword`, `.docx`→`application/vnd.openxmlformats-officedocument.wordprocessingml.document`, `.xls`→`application/vnd.ms-excel`, `.xlsx`→`application/vnd.openxmlformats-officedocument.spreadsheetml.sheet`, `*`→`application/octet-stream` | `GetContentTypeFromExtension` (private) |
| Virtual directory name | Hierarchy prefix with trailing `/` removed | `ListVirtualDirectoriesAsync` |

### Mock dependency graph

```
AzureBlobStorageServiceTests
  └─ AzureBlobStorageService (SUT)
       ├─ Mock<BlobServiceClient>
       │    └─ Mock<BlobContainerClient>
       │         ├─ CreateIfNotExistsAsync → success response
       │         ├─ GetBlobClient(name) → Mock<BlobClient>  [name captured via Callback]
       │         │    ├─ UploadAsync → success response     [BlobUploadOptions inspected]
       │         │    └─ Uri → synthetic Uri
       │         └─ GetBlobsByHierarchyAsync → AsyncPageable<BlobHierarchyItem>  [FR-6]
       ├─ Mock<IHttpClientFactory>
       │    └─ CreateClient("FileDownload") → HttpClient(StubHttpMessageHandler | BuildNoContentTypeHandler)
       └─ Mock<ILogger<AzureBlobStorageService>>
```

### AsyncPageable construction (FR-6)

```csharp
// Pattern for wrapping a list into a single-page AsyncPageable<T>
static AsyncPageable<T> CreateAsyncPageable<T>(IEnumerable<T> items)
{
    var page = Page<T>.FromValues(items.ToList(), continuationToken: null, response: Mock.Of<Azure.Response>());
    return AsyncPageable<T>.FromPages(new[] { page });
}
```

`BlobHierarchyItem` instances are built with `BlobsModelFactory.BlobHierarchyItem(prefix, blobItem)` where `prefix != null` produces an `IsPrefix == true` item and `blobItem != null` produces an `IsPrefix == false` item.
