# AzureBlobStorageService Coverage Gap Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Close the unit-test coverage gap in `AzureBlobStorageService` by adding tests for blob-name derivation, content-type/extension inference (both directions), the container cache "already-seen" branch, and virtual-directory prefix trimming.

**Architecture:** All work is confined to the existing xUnit test class `AzureBlobStorageServiceTests`. No production code changes. We add three private test helpers (mock wiring + blob-name capture, a no-content-type HTTP handler, and an `AsyncPageable<T>` builder), then six groups of `[Fact]`/`[Theory]` tests. Mocks use Moq; Azure SDK return types are faked with `Mock.Of<>`, `Page<T>.FromValues`, and `AsyncPageable<T>.FromPages`.

**Tech Stack:** .NET 8, xUnit, Moq, Azure.Storage.Blobs SDK (`BlobServiceClient`, `BlobContainerClient`, `BlobClient`, `BlobHierarchyItem`, `AsyncPageable<T>`), raw `Assert.*` (not FluentAssertions).

---

## Production behavior under test (read before starting)

These are the exact production code paths the tests must exercise. File:
`backend/src/Anela.Heblo.Application/Features/FileStorage/Services/AzureBlobStorageService.cs`

- `DownloadFromUrlAsync(string fileUrl, string containerName, string? blobName = null, CancellationToken = default)`:
  - When `blobName` is null/empty, blobName is derived from `Path.GetFileName(new Uri(fileUrl).LocalPath)` (FR-1).
  - When that filename is also empty (URL path ends in `/`), blobName becomes `downloaded-file-{Guid}{ext}` where `ext = GetExtensionFromContentType(response.Content.Headers.ContentType?.MediaType)` (FR-2, FR-3).
  - `contentType = response.Content.Headers.ContentType?.MediaType ?? GetContentTypeFromExtension(blobName)`. The fallback (FR-4) only runs when the HTTP response has **no** Content-Type header.
- `GetExtensionFromContentType` switch arms (FR-3): `image/jpeg`→`.jpg`, `image/png`→`.png`, `image/gif`→`.gif`, `image/webp`→`.webp`, `application/pdf`→`.pdf`, `text/plain`→`.txt`, `application/json`→`.json`, `application/xml`→`.xml`, default→`.bin`.
- `GetContentTypeFromExtension` switch arms (FR-4): `.jpg`/`.jpeg`→`image/jpeg`, `.png`→`image/png`, `.gif`→`image/gif`, `.webp`→`image/webp`, `.pdf`→`application/pdf`, `.txt`→`text/plain`, `.json`→`application/json`, `.xml`→`application/xml`, `.zip`→`application/zip`, `.doc`→`application/msword`, `.docx`→`application/vnd.openxmlformats-officedocument.wordprocessingml.document`, `.xls`→`application/vnd.ms-excel`, `.xlsx`→`application/vnd.openxmlformats-officedocument.spreadsheetml.sheet`, default→`application/octet-stream`. The method lowercases the extension first.
- `GetOrCreateContainerAsync` uses a `ConcurrentDictionary` + `TryAdd`; `CreateIfNotExistsAsync` runs only on first sight of a container name (FR-5). Note the production call site uses the **3-arg** overload `CreateIfNotExistsAsync(PublicAccessType.None, cancellationToken: ...)`, but Moq must set up the **4-arg** overload (with `BlobContainerEncryptionScopeOptions`) because that is the virtual method Moq intercepts — this matches the existing cache tests in the file (lines 463-464).
- `ListVirtualDirectoriesAsync` calls `GetBlobsByHierarchyAsync(prefix: null, delimiter: "/")`, keeps only `IsPrefix` items, and trims one trailing `/` from each prefix (FR-6).

## Important constraints (do not deviate)

- **No production code changes.** `GetExtensionFromContentType` and `GetContentTypeFromExtension` are private; test them indirectly through `DownloadFromUrlAsync`.
- **Match existing file style:** raw `Assert.*`, AAA comments (`// Arrange`, `// Act`, `// Assert`), `Mock<...>` from Moq, `Mock.Of<Azure.Response<...>>()` for SDK responses.
- To force the FR-4 fallback (`GetContentTypeFromExtension`), the HTTP response must have **no** `Content-Type` header. `StringContent` always sets `text/plain` automatically, so use a `ByteArrayContent` whose `ContentType` header is never set (helper `BuildNoContentTypeHandler`).
- To capture the blob name the service derived, record the string passed to `GetBlobClient(...)` via a Moq `Callback`.

---

### task: add-shared-test-helpers

**Files:**
- Modify: `backend/test/Anela.Heblo.Tests/Features/FileStorage/AzureBlobStorageServiceTests.cs`

- [ ] **Step 1: Add `using` directives for Azure pageable types**

At the top of the file the current usings are:

```csharp
using Anela.Heblo.Application.Features.FileStorage;
using Anela.Heblo.Application.Features.FileStorage.Services;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.Extensions.Logging;
using Moq;
using System.Net;
using System.Text;
using Xunit;
```

Replace that block with (adds `Azure` for `Page<T>`/`AsyncPageable<T>` and `System.Collections.Generic` is implicit, so only `Azure` is new):

```csharp
using Anela.Heblo.Application.Features.FileStorage;
using Anela.Heblo.Application.Features.FileStorage.Services;
using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.Extensions.Logging;
using Moq;
using System.Net;
using System.Text;
using Xunit;
```

- [ ] **Step 2: Add three private helpers before the `// Stub handlers` region**

Insert the following methods immediately **before** the line:

```csharp
    // ---------------------------------------------------------------------------
    // Stub handlers
    // ---------------------------------------------------------------------------
```

Code to insert:

```csharp
    // ---------------------------------------------------------------------------
    // Shared test helpers
    // ---------------------------------------------------------------------------

    /// <summary>
    /// Wires the blob service client so that GetBlobContainerClient(containerName) returns a
    /// container mock whose GetBlobClient(name) returns a blob mock. Every blob name passed to
    /// GetBlobClient is recorded into <paramref name="capturedBlobNames"/> so tests can assert
    /// on the name the service derived. UploadAsync and CreateIfNotExistsAsync are stubbed to succeed.
    /// </summary>
    private void SetupContainerAndBlobClient(
        string containerName,
        out Mock<BlobContainerClient> containerMock,
        out Mock<BlobClient> blobMock,
        out List<string> capturedBlobNames)
    {
        var names = new List<string>();
        capturedBlobNames = names;

        var container = new Mock<BlobContainerClient>();
        var blob = new Mock<BlobClient>();

        blob.Setup(x => x.Uri)
            .Returns(new Uri($"https://testaccount.blob.core.windows.net/{containerName}/blob"));
        blob.Setup(x => x.UploadAsync(
                It.IsAny<Stream>(),
                It.IsAny<BlobUploadOptions>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.FromResult(Mock.Of<Azure.Response<BlobContentInfo>>()));

        container.Setup(x => x.GetBlobClient(It.IsAny<string>()))
            .Callback<string>(name => names.Add(name))
            .Returns(blob.Object);
        container.Setup(x => x.CreateIfNotExistsAsync(
                It.IsAny<PublicAccessType>(),
                It.IsAny<IDictionary<string, string>>(),
                It.IsAny<BlobContainerEncryptionScopeOptions>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.FromResult(Mock.Of<Azure.Response<BlobContainerInfo>>()));

        _mockBlobServiceClient.Setup(x => x.GetBlobContainerClient(containerName))
            .Returns(container.Object);

        containerMock = container;
        blobMock = blob;
    }

    /// <summary>
    /// Builds a StubHttpMessageHandler whose 200 response carries <paramref name="responseBody"/>
    /// as ByteArrayContent with NO Content-Type header set. This forces the production code to fall
    /// back to GetContentTypeFromExtension(blobName) because response.Content.Headers.ContentType is null.
    /// </summary>
    private static StubHttpMessageHandler BuildNoContentTypeHandler(string responseBody = "test content")
    {
        var content = new ByteArrayContent(Encoding.UTF8.GetBytes(responseBody));
        // Deliberately do NOT set content.Headers.ContentType — leaves the media type null.
        return new StubHttpMessageHandler(HttpStatusCode.OK, overrideContent: content);
    }

    /// <summary>
    /// Wraps an in-memory sequence into an AsyncPageable&lt;T&gt; backed by a single page,
    /// so it can be returned from mocked Azure SDK enumeration methods.
    /// </summary>
    private static AsyncPageable<T> CreateAsyncPageable<T>(IEnumerable<T> items) where T : notnull
    {
        var page = Page<T>.FromValues(items.ToList(), continuationToken: null, response: Mock.Of<Azure.Response>());
        return AsyncPageable<T>.FromPages(new[] { page });
    }
```

- [ ] **Step 3: Build to verify helpers compile**

Run: `dotnet build backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj`
Expected: PASS (no new tests yet; helpers must compile). If `AsyncPageable<T>.FromPages` or `Page<T>.FromValues` fails to resolve, confirm the `using Azure;` directive from Step 1 was added.

- [ ] **Step 4: Commit**

Run: `git add backend/test/Anela.Heblo.Tests/Features/FileStorage/AzureBlobStorageServiceTests.cs && git commit -m "test(filestorage): add shared helpers for blob-name capture and pageable doubles"`

---

### task: fr1-blobname-from-url-path

**Files:**
- Modify: `backend/test/Anela.Heblo.Tests/Features/FileStorage/AzureBlobStorageServiceTests.cs`

- [ ] **Step 1: Add FR-1 test**

Insert after the existing `DownloadAndUploadFromUrl_DifferentContentTypes_ShouldGenerateCorrectExtension` theory (i.e. after the closing `}` of that method, around line 212), inside the class:

```csharp
    // ---------------------------------------------------------------------------
    // FR-1: blobName derived from the URL path filename
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task DownloadFromUrlAsync_UrlWithFilename_UsesBlobNameFromPath()
    {
        // Arrange
        var fileUrl = "https://example.com/folder/report.pdf";
        var containerName = "documents";

        var handler = new StubHttpMessageHandler(HttpStatusCode.OK, "test content");
        var client = new HttpClient(handler) { BaseAddress = new Uri("http://test/") };
        _mockHttpClientFactory
            .Setup(f => f.CreateClient(FileStorageModule.FileDownloadClientName))
            .Returns(client);

        SetupContainerAndBlobClient(containerName, out _, out _, out var capturedBlobNames);

        // Act
        await _service.DownloadFromUrlAsync(fileUrl, containerName);

        // Assert — blob name comes from the URL path filename, not a generated GUID
        Assert.Contains("report.pdf", capturedBlobNames);
    }
```

- [ ] **Step 2: Run the FR-1 test**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~DownloadFromUrlAsync_UrlWithFilename_UsesBlobNameFromPath"`
Expected: PASS

- [ ] **Step 3: Commit**

Run: `git add -A && git commit -m "test(filestorage): FR-1 blobName derived from URL path filename"`

---

### task: fr2-generated-blobname-extension

**Files:**
- Modify: `backend/test/Anela.Heblo.Tests/Features/FileStorage/AzureBlobStorageServiceTests.cs`

These tests use a URL whose path ends in `/` so `Path.GetFileName(uri.LocalPath)` returns an empty string, forcing the `downloaded-file-{Guid}{ext}` branch. The extension is decided by `GetExtensionFromContentType(response Content-Type)`, so the handler must carry an explicit Content-Type header.

- [ ] **Step 1: Add FR-2 tests**

Insert after the FR-1 test added in the previous task:

```csharp
    // ---------------------------------------------------------------------------
    // FR-2: no filename in URL → generated "downloaded-file-{guid}{ext}" blob name
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task DownloadFromUrlAsync_UrlWithNoFilename_KnownContentType_UsesPrefixAndExtension()
    {
        // Arrange — URL path ends with '/', so Path.GetFileName returns empty and a name is generated.
        var fileUrl = "https://example.com/files/";
        var containerName = "documents";

        var responseContent = new StringContent("test content");
        responseContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/png");
        var handler = new StubHttpMessageHandler(HttpStatusCode.OK, overrideContent: responseContent);
        var client = new HttpClient(handler) { BaseAddress = new Uri("http://test/") };
        _mockHttpClientFactory
            .Setup(f => f.CreateClient(FileStorageModule.FileDownloadClientName))
            .Returns(client);

        SetupContainerAndBlobClient(containerName, out _, out _, out var capturedBlobNames);

        // Act
        await _service.DownloadFromUrlAsync(fileUrl, containerName);

        // Assert — generated name uses the "downloaded-file-" prefix and the content-type extension
        var generatedName = Assert.Single(capturedBlobNames);
        Assert.StartsWith("downloaded-file-", generatedName);
        Assert.EndsWith(".png", generatedName);
    }

    [Fact]
    public async Task DownloadFromUrlAsync_UrlWithNoFilename_UnknownContentType_UsesBinExtension()
    {
        // Arrange — unknown content type maps to the ".bin" fallback in GetExtensionFromContentType.
        var fileUrl = "https://example.com/files/";
        var containerName = "documents";

        var responseContent = new StringContent("test content");
        responseContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/x-unknown");
        var handler = new StubHttpMessageHandler(HttpStatusCode.OK, overrideContent: responseContent);
        var client = new HttpClient(handler) { BaseAddress = new Uri("http://test/") };
        _mockHttpClientFactory
            .Setup(f => f.CreateClient(FileStorageModule.FileDownloadClientName))
            .Returns(client);

        SetupContainerAndBlobClient(containerName, out _, out _, out var capturedBlobNames);

        // Act
        await _service.DownloadFromUrlAsync(fileUrl, containerName);

        // Assert
        var generatedName = Assert.Single(capturedBlobNames);
        Assert.StartsWith("downloaded-file-", generatedName);
        Assert.EndsWith(".bin", generatedName);
    }
```

- [ ] **Step 2: Run the FR-2 tests**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~DownloadFromUrlAsync_UrlWithNoFilename"`
Expected: PASS (2 tests)

- [ ] **Step 3: Commit**

Run: `git add -A && git commit -m "test(filestorage): FR-2 generated blobName uses prefix + content-type extension"`

---

### task: fr3-extension-from-contenttype-all-arms

**Files:**
- Modify: `backend/test/Anela.Heblo.Tests/Features/FileStorage/AzureBlobStorageServiceTests.cs`

This exercises every arm of `GetExtensionFromContentType` (indirectly, via the FR-2 generated-name path). The URL path ends in `/` so the generated-name branch always runs; the response Content-Type drives the extension.

- [ ] **Step 1: Add FR-3 theory**

Insert after the FR-2 tests:

```csharp
    // ---------------------------------------------------------------------------
    // FR-3: GetExtensionFromContentType — all switch arms (via generated blob name)
    // ---------------------------------------------------------------------------

    [Theory]
    [InlineData("image/jpeg", ".jpg")]
    [InlineData("image/png", ".png")]
    [InlineData("image/gif", ".gif")]
    [InlineData("image/webp", ".webp")]
    [InlineData("application/pdf", ".pdf")]
    [InlineData("text/plain", ".txt")]
    [InlineData("application/json", ".json")]
    [InlineData("application/xml", ".xml")]
    [InlineData("application/x-unknown", ".bin")]
    public async Task DownloadFromUrlAsync_NoFilenameUrl_ContentTypeToExtension_AllArms(
        string contentType, string expectedExtension)
    {
        // Arrange — URL ends with '/' to force the generated "downloaded-file-{guid}{ext}" branch.
        var fileUrl = "https://example.com/files/";
        var containerName = "documents";

        var responseContent = new StringContent("test content");
        responseContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(contentType);
        var handler = new StubHttpMessageHandler(HttpStatusCode.OK, overrideContent: responseContent);
        var client = new HttpClient(handler) { BaseAddress = new Uri("http://test/") };
        _mockHttpClientFactory
            .Setup(f => f.CreateClient(FileStorageModule.FileDownloadClientName))
            .Returns(client);

        SetupContainerAndBlobClient(containerName, out _, out _, out var capturedBlobNames);

        // Act
        await _service.DownloadFromUrlAsync(fileUrl, containerName);

        // Assert
        var generatedName = Assert.Single(capturedBlobNames);
        Assert.EndsWith(expectedExtension, generatedName);
    }
```

- [ ] **Step 2: Run the FR-3 theory**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~DownloadFromUrlAsync_NoFilenameUrl_ContentTypeToExtension_AllArms"`
Expected: PASS (9 cases)

- [ ] **Step 3: Commit**

Run: `git add -A && git commit -m "test(filestorage): FR-3 GetExtensionFromContentType all switch arms"`

---

### task: fr4-contenttype-from-extension-all-arms

**Files:**
- Modify: `backend/test/Anela.Heblo.Tests/Features/FileStorage/AzureBlobStorageServiceTests.cs`

This replaces the existing placeholder theory with a real one. `GetContentTypeFromExtension` only runs when the HTTP response has **no** Content-Type header, so use `BuildNoContentTypeHandler`. The blob name is supplied explicitly (with the extension under test) so the derived content type is asserted via the `BlobUploadOptions.HttpHeaders.ContentType` passed to `UploadAsync`.

- [ ] **Step 1: Remove the placeholder theory**

Delete this entire existing method (lines ~117-133):

```csharp
    [Theory]
    [InlineData(".jpg", "image/jpeg")]
    [InlineData(".jpeg", "image/jpeg")]
    [InlineData(".png", "image/png")]
    [InlineData(".pdf", "application/pdf")]
    [InlineData(".txt", "text/plain")]
    [InlineData(".json", "application/json")]
    [InlineData(".unknown", "application/octet-stream")]
    public void GetContentTypeFromExtension_DifferentExtensions_ShouldReturnCorrectContentType(string extension, string expectedContentType)
    {
        // This test would require making the private method internal or using reflection
        // For now, we'll test it indirectly through other methods
        var fileName = $"test{extension}";
        // The GetContentTypeFromExtension method is private, so we test it indirectly
        Assert.NotEmpty(fileName); // Placeholder assertion
        Assert.NotEmpty(expectedContentType); // Verify expected content type is provided
    }
```

- [ ] **Step 2: Add the real FR-4 theory plus the uppercase-extension fact in its place**

Insert (in the same location, under the existing `// GetContentTypeFromExtension (tested indirectly)` comment header):

```csharp
    [Theory]
    [InlineData("file.jpg", "image/jpeg")]
    [InlineData("file.jpeg", "image/jpeg")]
    [InlineData("file.png", "image/png")]
    [InlineData("file.gif", "image/gif")]
    [InlineData("file.webp", "image/webp")]
    [InlineData("file.pdf", "application/pdf")]
    [InlineData("file.txt", "text/plain")]
    [InlineData("file.json", "application/json")]
    [InlineData("file.xml", "application/xml")]
    [InlineData("file.zip", "application/zip")]
    [InlineData("file.doc", "application/msword")]
    [InlineData("file.docx", "application/vnd.openxmlformats-officedocument.wordprocessingml.document")]
    [InlineData("file.xls", "application/vnd.ms-excel")]
    [InlineData("file.xlsx", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet")]
    [InlineData("file.unknown", "application/octet-stream")]
    public async Task DownloadFromUrlAsync_NoResponseContentType_InfersContentTypeFromExtension(
        string blobName, string expectedContentType)
    {
        // Arrange — response has NO Content-Type header, so the service infers it from the blob extension.
        var fileUrl = "https://example.com/file";
        var containerName = "documents";

        var handler = BuildNoContentTypeHandler();
        var client = new HttpClient(handler) { BaseAddress = new Uri("http://test/") };
        _mockHttpClientFactory
            .Setup(f => f.CreateClient(FileStorageModule.FileDownloadClientName))
            .Returns(client);

        SetupContainerAndBlobClient(containerName, out _, out var blobMock, out _);

        // Act — pass the blob name explicitly so the inferred content type depends only on its extension
        await _service.DownloadFromUrlAsync(fileUrl, containerName, blobName);

        // Assert — UploadAsync received the content type inferred from the extension
        blobMock.Verify(x => x.UploadAsync(
            It.IsAny<Stream>(),
            It.Is<BlobUploadOptions>(opts => opts.HttpHeaders.ContentType == expectedContentType),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DownloadFromUrlAsync_NoResponseContentType_UppercaseExtension_InfersContentType()
    {
        // Arrange — GetContentTypeFromExtension lowercases the extension before matching.
        var fileUrl = "https://example.com/file";
        var containerName = "documents";

        var handler = BuildNoContentTypeHandler();
        var client = new HttpClient(handler) { BaseAddress = new Uri("http://test/") };
        _mockHttpClientFactory
            .Setup(f => f.CreateClient(FileStorageModule.FileDownloadClientName))
            .Returns(client);

        SetupContainerAndBlobClient(containerName, out _, out var blobMock, out _);

        // Act — uppercase extension must still resolve to image/jpeg
        await _service.DownloadFromUrlAsync(fileUrl, containerName, "PHOTO.JPG");

        // Assert
        blobMock.Verify(x => x.UploadAsync(
            It.IsAny<Stream>(),
            It.Is<BlobUploadOptions>(opts => opts.HttpHeaders.ContentType == "image/jpeg"),
            It.IsAny<CancellationToken>()), Times.Once);
    }
```

- [ ] **Step 3: Run the FR-4 tests**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~DownloadFromUrlAsync_NoResponseContentType"`
Expected: PASS (15 theory cases + 1 fact = 16)

- [ ] **Step 4: Commit**

Run: `git add -A && git commit -m "test(filestorage): FR-4 replace placeholder with real GetContentTypeFromExtension coverage"`

---

### task: fr5-container-cache-download-path

**Files:**
- Modify: `backend/test/Anela.Heblo.Tests/Features/FileStorage/AzureBlobStorageServiceTests.cs`

Verifies the container cache "already-seen" branch is reached via the `DownloadFromUrlAsync` → `UploadAsync` → `GetOrCreateContainerAsync` path. Use a fresh service instance so `_containerExists` starts empty.

- [ ] **Step 1: Add FR-5 test**

Insert after the FR-4 tests (or anywhere inside the class, e.g. after the FR-3 theory):

```csharp
    // ---------------------------------------------------------------------------
    // FR-5: container cache — CreateIfNotExists runs once across repeat downloads
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task DownloadFromUrlAsync_CalledTwice_SameContainer_CallsCreateIfNotExistsOnce()
    {
        // Arrange — fresh instance so the _containerExists cache starts empty.
        var mockBlobServiceClient = new Mock<BlobServiceClient>();
        var mockLogger = new Mock<ILogger<AzureBlobStorageService>>();

        var handler = new StubHttpMessageHandler(HttpStatusCode.OK, "test content");
        var client = new HttpClient(handler) { BaseAddress = new Uri("http://test/") };
        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(f => f.CreateClient(FileStorageModule.FileDownloadClientName)).Returns(client);

        var service = new AzureBlobStorageService(
            mockBlobServiceClient.Object,
            factory.Object,
            mockLogger.Object);

        var containerName = "documents";
        var mockContainerClient = new Mock<BlobContainerClient>();
        var mockBlobClient = new Mock<BlobClient>();

        mockBlobClient.Setup(x => x.Uri)
            .Returns(new Uri($"https://test.blob.core.windows.net/{containerName}/file.pdf"));
        mockBlobClient.Setup(x => x.UploadAsync(
                It.IsAny<Stream>(), It.IsAny<BlobUploadOptions>(), It.IsAny<CancellationToken>()))
            .Returns(Task.FromResult(Mock.Of<Azure.Response<BlobContentInfo>>()));
        mockContainerClient.Setup(x => x.GetBlobClient(It.IsAny<string>())).Returns(mockBlobClient.Object);
        mockContainerClient.Setup(x => x.CreateIfNotExistsAsync(
                It.IsAny<PublicAccessType>(),
                It.IsAny<IDictionary<string, string>>(),
                It.IsAny<BlobContainerEncryptionScopeOptions>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.FromResult(Mock.Of<Azure.Response<BlobContainerInfo>>()));
        mockBlobServiceClient.Setup(x => x.GetBlobContainerClient(containerName))
            .Returns(mockContainerClient.Object);

        // Act — two downloads to the same container
        await service.DownloadFromUrlAsync("https://example.com/a.pdf", containerName);
        await service.DownloadFromUrlAsync("https://example.com/b.pdf", containerName);

        // Assert — second download hits the cached "already-seen" branch, so no second create
        mockContainerClient.Verify(
            x => x.CreateIfNotExistsAsync(
                It.IsAny<PublicAccessType>(),
                It.IsAny<IDictionary<string, string>>(),
                It.IsAny<BlobContainerEncryptionScopeOptions>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }
```

- [ ] **Step 2: Run the FR-5 test**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~DownloadFromUrlAsync_CalledTwice_SameContainer_CallsCreateIfNotExistsOnce"`
Expected: PASS

- [ ] **Step 3: Commit**

Run: `git add -A && git commit -m "test(filestorage): FR-5 container cache reached via download path"`

---

### task: fr6-virtual-directories-trailing-slash

**Files:**
- Modify: `backend/test/Anela.Heblo.Tests/Features/FileStorage/AzureBlobStorageServiceTests.cs`

Tests `ListVirtualDirectoriesAsync`. The mocked `GetBlobsByHierarchyAsync` returns an `AsyncPageable<BlobHierarchyItem>` built with the `CreateAsyncPageable` helper. `BlobHierarchyItem` instances are created via the SDK model factory `BlobsModelFactory.BlobHierarchyItem(prefix, blobItem)` — pass a non-null `prefix` to make `IsPrefix` true.

- [ ] **Step 1: Add FR-6 tests**

Insert after the FR-5 test:

```csharp
    // ---------------------------------------------------------------------------
    // FR-6: ListVirtualDirectoriesAsync — trailing-slash trimming
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task ListVirtualDirectoriesAsync_TrimsTrailingSlash_FromPrefixes()
    {
        // Arrange
        var containerName = "documents";
        var mockContainerClient = new Mock<BlobContainerClient>();

        var items = new[]
        {
            BlobsModelFactory.BlobHierarchyItem("invoices/", null),
            BlobsModelFactory.BlobHierarchyItem("reports/", null),
        };

        mockContainerClient.Setup(x => x.GetBlobsByHierarchyAsync(
                It.IsAny<BlobTraits>(),
                It.IsAny<BlobStates>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .Returns(CreateAsyncPageable(items));
        _mockBlobServiceClient.Setup(x => x.GetBlobContainerClient(containerName))
            .Returns(mockContainerClient.Object);

        // Act
        var result = await _service.ListVirtualDirectoriesAsync(containerName);

        // Assert — each prefix has exactly one trailing slash trimmed
        Assert.Equal(2, result.Count);
        Assert.Contains("invoices", result);
        Assert.Contains("reports", result);
    }

    [Fact]
    public async Task ListVirtualDirectoriesAsync_EmptyContainer_ReturnsEmptyList()
    {
        // Arrange
        var containerName = "documents";
        var mockContainerClient = new Mock<BlobContainerClient>();

        mockContainerClient.Setup(x => x.GetBlobsByHierarchyAsync(
                It.IsAny<BlobTraits>(),
                It.IsAny<BlobStates>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .Returns(CreateAsyncPageable(Array.Empty<BlobHierarchyItem>()));
        _mockBlobServiceClient.Setup(x => x.GetBlobContainerClient(containerName))
            .Returns(mockContainerClient.Object);

        // Act
        var result = await _service.ListVirtualDirectoriesAsync(containerName);

        // Assert
        Assert.Empty(result);
    }
```

- [ ] **Step 2: Run the FR-6 tests**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~ListVirtualDirectoriesAsync"`
Expected: PASS (2 tests)

Note: If `GetBlobsByHierarchyAsync` overload resolution fails to match (the SDK signature is `(BlobTraits, BlobStates, string delimiter, string prefix, CancellationToken)`), keep the five `It.IsAny<...>` arguments in that exact order — they correspond to the positional parameters of the virtual method Moq intercepts.

- [ ] **Step 3: Commit**

Run: `git add -A && git commit -m "test(filestorage): FR-6 ListVirtualDirectoriesAsync trims trailing slash"`

---

### task: full-suite-verification

**Files:**
- Modify: none (verification only)

- [ ] **Step 1: Run the full test class**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~AzureBlobStorageServiceTests"`
Expected: PASS (all tests in the class, including the pre-existing ones)

- [ ] **Step 2: Build and format the backend**

Run: `dotnet build backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj && dotnet format backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --verify-no-changes`
Expected: build PASS. If `dotnet format --verify-no-changes` reports changes, run `dotnet format backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj`, then re-commit.

- [ ] **Step 3: Final commit (only if format made changes)**

Run: `git add -A && git commit -m "style(filestorage): apply dotnet format to AzureBlobStorageService tests"`
(Skip if there is nothing to commit.)
