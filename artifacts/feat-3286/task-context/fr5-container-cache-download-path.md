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

