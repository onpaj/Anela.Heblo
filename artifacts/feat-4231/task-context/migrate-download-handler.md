### task: migrate-download-handler

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/ExpeditionListArchive/UseCases/DownloadExpeditionList/DownloadExpeditionListHandler.cs`
- Modify: `backend/test/Anela.Heblo.Tests/ExpeditionListArchive/DownloadExpeditionListHandlerTests.cs`

- [ ] **Step 1: Update the handler**

Replace the full contents of `DownloadExpeditionListHandler.cs`:

```csharp
using Anela.Heblo.Application.Features.ExpeditionListArchive.Contracts;
using MediatR;
using Microsoft.Extensions.Options;

namespace Anela.Heblo.Application.Features.ExpeditionListArchive.UseCases.DownloadExpeditionList;

public class DownloadExpeditionListHandler : IRequestHandler<DownloadExpeditionListRequest, DownloadExpeditionListResponse>
{
    private readonly IExpeditionListArchiveBlobStore _blobStore;
    private readonly string _containerName;

    public DownloadExpeditionListHandler(IExpeditionListArchiveBlobStore blobStore, IOptions<ExpeditionListArchiveOptions> options)
    {
        _blobStore = blobStore;
        _containerName = options.Value.BlobContainerName;
    }

    public async Task<DownloadExpeditionListResponse> Handle(DownloadExpeditionListRequest request, CancellationToken cancellationToken)
    {
        if (!BlobPathValidator.IsValid(request.BlobPath))
        {
            return DownloadExpeditionListResponse.Fail();
        }

        var stream = await _blobStore.DownloadAsync(_containerName, request.BlobPath, cancellationToken);
        var fileName = Path.GetFileName(request.BlobPath);

        return new DownloadExpeditionListResponse
        {
            Success = true,
            Stream = stream,
            ContentType = "application/pdf",
            FileName = fileName
        };
    }
}
```

Note: `using Anela.Heblo.Domain.Features.FileStorage;` is removed entirely; the only other change is the field/parameter type and name (`_blobStorageService` → `_blobStore`) and the call site (`_blobStorageService.DownloadAsync` → `_blobStore.DownloadAsync`). No other logic changes.

- [ ] **Step 2: Update the test**

Replace the full contents of `DownloadExpeditionListHandlerTests.cs`:

```csharp
using Anela.Heblo.Application.Features.ExpeditionListArchive;
using Anela.Heblo.Application.Features.ExpeditionListArchive.Contracts;
using Anela.Heblo.Application.Features.ExpeditionListArchive.UseCases.DownloadExpeditionList;
using Anela.Heblo.Application.Shared;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.ExpeditionListArchive;

public class DownloadExpeditionListHandlerTests
{
    private readonly Mock<IExpeditionListArchiveBlobStore> _blobStoreMock;
    private readonly DownloadExpeditionListHandler _handler;
    private const string ContainerName = "expedition-lists";

    public DownloadExpeditionListHandlerTests()
    {
        _blobStoreMock = new Mock<IExpeditionListArchiveBlobStore>();
        _handler = new DownloadExpeditionListHandler(_blobStoreMock.Object, Options.Create(new ExpeditionListArchiveOptions()));
    }

    [Fact]
    public async Task Handle_ValidBlobPath_ReturnsBlobStream()
    {
        // Arrange
        var blobPath = "2026-03-25/picking-list-001.pdf";
        var expectedStream = new MemoryStream(new byte[] { 1, 2, 3 });

        _blobStoreMock
            .Setup(s => s.DownloadAsync(ContainerName, blobPath, default))
            .ReturnsAsync(expectedStream);

        var request = new DownloadExpeditionListRequest { BlobPath = blobPath };

        // Act
        var result = await _handler.Handle(request, default);

        // Assert
        Assert.True(result.Success);
        Assert.Equal(expectedStream, result.Stream);
        Assert.Equal("application/pdf", result.ContentType);
        Assert.Equal("picking-list-001.pdf", result.FileName);
    }

    [Theory]
    [InlineData("../secret/file.pdf")]
    [InlineData("2026-03-25/../../../etc/passwd")]
    [InlineData("invalid-date/file.pdf")]
    [InlineData("2026-03-25/file.exe")]
    [InlineData("")]
    public async Task Handle_InvalidBlobPath_ReturnsFailure(string blobPath)
    {
        // Arrange
        var request = new DownloadExpeditionListRequest { BlobPath = blobPath };

        // Act
        var result = await _handler.Handle(request, default);

        // Assert
        Assert.False(result.Success);
        Assert.Equal(ErrorCodes.InvalidBlobPath, result.ErrorCode);
        Assert.Null(result.Stream);

        _blobStoreMock.Verify(
            s => s.DownloadAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }
}
```

- [ ] **Step 3: Run the test to verify it passes**

Run: `cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~DownloadExpeditionListHandlerTests"`
Expected: PASS, 2 tests (6 cases with Theory), 0 failed.

- [ ] **Step 4: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/ExpeditionListArchive/UseCases/DownloadExpeditionList/DownloadExpeditionListHandler.cs \
        backend/test/Anela.Heblo.Tests/ExpeditionListArchive/DownloadExpeditionListHandlerTests.cs
git commit -m "refactor(expedition-list-archive): migrate DownloadExpeditionListHandler to IExpeditionListArchiveBlobStore"
```

---

