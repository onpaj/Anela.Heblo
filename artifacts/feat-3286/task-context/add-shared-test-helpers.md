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

