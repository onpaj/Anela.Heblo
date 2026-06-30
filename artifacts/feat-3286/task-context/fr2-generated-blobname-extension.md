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

