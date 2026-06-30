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

