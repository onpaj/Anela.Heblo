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

