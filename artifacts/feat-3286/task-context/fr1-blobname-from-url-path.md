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

