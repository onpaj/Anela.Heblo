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

