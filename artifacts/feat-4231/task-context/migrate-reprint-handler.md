### task: migrate-reprint-handler

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/ExpeditionListArchive/UseCases/ReprintExpeditionList/ReprintExpeditionListHandler.cs`
- Modify: `backend/src/Anela.Heblo.Application/Features/ExpeditionListArchive/ExpeditionListArchiveModule.cs`
- Modify: `backend/test/Anela.Heblo.Tests/ExpeditionListArchive/ReprintExpeditionListHandlerTests.cs`

- [ ] **Step 1: Update the handler**

Replace the full contents of `ReprintExpeditionListHandler.cs`:

```csharp
using Anela.Heblo.Application.Features.ExpeditionListArchive.Contracts;
using Anela.Heblo.Application.Shared.Printing;
using MediatR;
using Microsoft.Extensions.Options;

namespace Anela.Heblo.Application.Features.ExpeditionListArchive.UseCases.ReprintExpeditionList;

public class ReprintExpeditionListHandler : IRequestHandler<ReprintExpeditionListRequest, ReprintExpeditionListResponse>
{
    private readonly IExpeditionListArchiveBlobStore _blobStore;
    private readonly IPrintQueueSink _cupsSink;
    private readonly ITemporaryFileAccessor _temporaryFileAccessor;
    private readonly string _containerName;

    public ReprintExpeditionListHandler(
        IExpeditionListArchiveBlobStore blobStore,
        IPrintQueueSink cupsSink,
        ITemporaryFileAccessor temporaryFileAccessor,
        IOptions<ExpeditionListArchiveOptions> options)
    {
        _blobStore = blobStore;
        _cupsSink = cupsSink;
        _temporaryFileAccessor = temporaryFileAccessor;
        _containerName = options.Value.BlobContainerName;
    }

    public async Task<ReprintExpeditionListResponse> Handle(ReprintExpeditionListRequest request, CancellationToken cancellationToken)
    {
        if (!BlobPathValidator.IsValid(request.BlobPath))
        {
            return ReprintExpeditionListResponse.Fail();
        }

        string? tempFile = null;
        try
        {
            await using var blobStream = await _blobStore.DownloadAsync(_containerName, request.BlobPath, cancellationToken);
            tempFile = await _temporaryFileAccessor.CreateFromStreamAsync(blobStream, ".pdf", cancellationToken);

            await _cupsSink.SendAsync(new[] { tempFile }, cancellationToken);
            return new ReprintExpeditionListResponse { Success = true };
        }
        finally
        {
            if (tempFile != null)
            {
                _temporaryFileAccessor.DeleteIfExists(tempFile);
            }
        }
    }
}
```

Note: `using Anela.Heblo.Domain.Features.FileStorage;` is removed entirely; only the field/parameter type and name (`_blobStorageService` → `_blobStore`) and the call site change. All try/finally, error propagation, and cleanup logic is byte-for-byte unchanged.

- [ ] **Step 2: Update the manual DI factory in `ExpeditionListArchiveModule.cs`**

Replace the full contents of `ExpeditionListArchiveModule.cs`:

```csharp
using Anela.Heblo.Application.Features.ExpeditionListArchive.Contracts;
using Anela.Heblo.Application.Features.ExpeditionListArchive.UseCases.ReprintExpeditionList;
using Anela.Heblo.Application.Shared.Printing;
using MediatR;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Anela.Heblo.Application.Features.ExpeditionListArchive;

public static class ExpeditionListArchiveModule
{
    public static IServiceCollection AddExpeditionListArchiveModule(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<ExpeditionListArchiveOptions>(configuration.GetSection(ExpeditionListArchiveOptions.ConfigurationKey));

        // ReprintExpeditionListHandler needs the keyed "cups" IPrintQueueSink when available
        // (production/staging). In environments where only the non-keyed sink is registered
        // (e.g. FileSystem in development/test), we fall back to the non-keyed registration.
        // This explicit factory overrides MediatR's auto-registration so the correct sink is injected.
        services.AddTransient<IRequestHandler<ReprintExpeditionListRequest, ReprintExpeditionListResponse>>(provider =>
        {
            var blobStore = provider.GetRequiredService<IExpeditionListArchiveBlobStore>();
            var cupsSink = provider.GetKeyedService<IPrintQueueSink>("cups")
                ?? provider.GetRequiredService<IPrintQueueSink>();
            var temporaryFileAccessor = provider.GetRequiredService<ITemporaryFileAccessor>();
            var options = provider.GetRequiredService<IOptions<ExpeditionListArchiveOptions>>();
            return new ReprintExpeditionListHandler(blobStore, cupsSink, temporaryFileAccessor, options);
        });

        return services;
    }
}
```

Note: `using Anela.Heblo.Domain.Features.FileStorage;` is removed; `provider.GetRequiredService<IBlobStorageService>()` becomes `provider.GetRequiredService<IExpeditionListArchiveBlobStore>()`; the local variable is renamed `blobStore` for clarity, matching the handler's own field name. This resolves correctly because task `adapter-and-di` already registered `IExpeditionListArchiveBlobStore` in `FileStorageModule`, and `FileStorageModule.AddFileStorageModule` and `ExpeditionListArchiveModule.AddExpeditionListArchiveModule` are both called from `ApplicationModule.cs` before the app starts serving requests.

- [ ] **Step 3: Update the test**

Replace the full contents of `ReprintExpeditionListHandlerTests.cs`:

```csharp
using Anela.Heblo.Application.Features.ExpeditionListArchive;
using Anela.Heblo.Application.Features.ExpeditionListArchive.Contracts;
using Anela.Heblo.Application.Features.ExpeditionListArchive.UseCases.ReprintExpeditionList;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Application.Shared.Printing;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.ExpeditionListArchive;

public class ReprintExpeditionListHandlerTests
{
    private readonly Mock<IExpeditionListArchiveBlobStore> _blobStoreMock;
    private readonly Mock<IPrintQueueSink> _cupsSinkMock;
    private readonly Mock<ITemporaryFileAccessor> _temporaryFileAccessorMock;
    private readonly ReprintExpeditionListHandler _handler;
    private const string ContainerName = "expedition-lists";

    public ReprintExpeditionListHandlerTests()
    {
        _blobStoreMock = new Mock<IExpeditionListArchiveBlobStore>();
        _cupsSinkMock = new Mock<IPrintQueueSink>();
        _temporaryFileAccessorMock = new Mock<ITemporaryFileAccessor>();
        _handler = new ReprintExpeditionListHandler(
            _blobStoreMock.Object,
            _cupsSinkMock.Object,
            _temporaryFileAccessorMock.Object,
            Options.Create(new ExpeditionListArchiveOptions()));
    }

    [Fact]
    public async Task Handle_ValidBlobPath_DownloadsAndSendsToCupsSink()
    {
        // Arrange
        var blobPath = "2026-03-25/picking-list-001.pdf";
        var pdfContent = new byte[] { 0x25, 0x50, 0x44, 0x46 }; // PDF magic bytes
        var blobStream = new MemoryStream(pdfContent);
        const string tempPath = "/tmp/generated-guid-001.pdf";

        _blobStoreMock
            .Setup(s => s.DownloadAsync(ContainerName, blobPath, default))
            .ReturnsAsync(blobStream);

        _temporaryFileAccessorMock
            .Setup(a => a.CreateFromStreamAsync(blobStream, ".pdf", default))
            .ReturnsAsync(tempPath);

        _cupsSinkMock
            .Setup(s => s.SendAsync(It.IsAny<IEnumerable<string>>(), default))
            .Returns(Task.CompletedTask);

        var request = new ReprintExpeditionListRequest { BlobPath = blobPath };

        // Act
        var result = await _handler.Handle(request, default);

        // Assert
        Assert.True(result.Success);
        _blobStoreMock.Verify(s => s.DownloadAsync(ContainerName, blobPath, default), Times.Once);
        _temporaryFileAccessorMock.Verify(a => a.CreateFromStreamAsync(blobStream, ".pdf", default), Times.Once);
        _cupsSinkMock.Verify(
            s => s.SendAsync(It.Is<IEnumerable<string>>(paths => paths.Single() == tempPath), default),
            Times.Once);
    }

    [Fact]
    public async Task Handle_SuccessfulSend_DeletesTempFile()
    {
        // Arrange
        var blobPath = "2026-03-25/picking-list-002.pdf";
        var pdfContent = new byte[] { 0x25, 0x50, 0x44, 0x46 };
        var blobStream = new MemoryStream(pdfContent);
        const string tempPath = "/tmp/generated-guid-002.pdf";

        _blobStoreMock
            .Setup(s => s.DownloadAsync(ContainerName, blobPath, default))
            .ReturnsAsync(blobStream);
        _temporaryFileAccessorMock
            .Setup(a => a.CreateFromStreamAsync(blobStream, ".pdf", default))
            .ReturnsAsync(tempPath);
        _cupsSinkMock
            .Setup(s => s.SendAsync(It.IsAny<IEnumerable<string>>(), default))
            .Returns(Task.CompletedTask);

        var request = new ReprintExpeditionListRequest { BlobPath = blobPath };

        // Act
        var result = await _handler.Handle(request, default);

        // Assert
        Assert.True(result.Success);
        _temporaryFileAccessorMock.Verify(a => a.DeleteIfExists(tempPath), Times.Once);
    }

    [Fact]
    public async Task Handle_SendAsyncThrows_StillDeletesTempFileAndPropagates()
    {
        // Arrange
        var blobPath = "2026-03-25/picking-list-003.pdf";
        var blobStream = new MemoryStream(new byte[] { 0x25, 0x50, 0x44, 0x46 });
        const string tempPath = "/tmp/generated-guid-003.pdf";

        _blobStoreMock
            .Setup(s => s.DownloadAsync(ContainerName, blobPath, default))
            .ReturnsAsync(blobStream);
        _temporaryFileAccessorMock
            .Setup(a => a.CreateFromStreamAsync(blobStream, ".pdf", default))
            .ReturnsAsync(tempPath);
        _cupsSinkMock
            .Setup(s => s.SendAsync(It.IsAny<IEnumerable<string>>(), default))
            .ThrowsAsync(new IOException("cups unavailable"));

        var request = new ReprintExpeditionListRequest { BlobPath = blobPath };

        // Act & Assert
        await Assert.ThrowsAsync<IOException>(() => _handler.Handle(request, default));

        _temporaryFileAccessorMock.Verify(a => a.DeleteIfExists(tempPath), Times.Once);
    }

    [Fact]
    public async Task Handle_BlobDownloadFails_CreatesNothing()
    {
        // Arrange
        var blobPath = "2026-03-25/picking-list-004.pdf";

        _blobStoreMock
            .Setup(s => s.DownloadAsync(ContainerName, blobPath, default))
            .ThrowsAsync(new IOException("blob unavailable"));

        var request = new ReprintExpeditionListRequest { BlobPath = blobPath };

        // Act & Assert
        await Assert.ThrowsAsync<IOException>(() => _handler.Handle(request, default));

        _temporaryFileAccessorMock.Verify(
            a => a.CreateFromStreamAsync(It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _temporaryFileAccessorMock.Verify(
            a => a.DeleteIfExists(It.IsAny<string>()),
            Times.Never);
        _cupsSinkMock.Verify(
            s => s.SendAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_InvalidBlobPath_ReturnsFailureWithoutCallingBlob()
    {
        // Arrange
        var request = new ReprintExpeditionListRequest { BlobPath = "../malicious/path.pdf" };

        // Act
        var result = await _handler.Handle(request, default);

        // Assert
        Assert.False(result.Success);
        Assert.Equal(ErrorCodes.InvalidBlobPath, result.ErrorCode);
        _blobStoreMock.Verify(
            s => s.DownloadAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _cupsSinkMock.Verify(
            s => s.SendAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _temporaryFileAccessorMock.Verify(
            a => a.CreateFromStreamAsync(It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _temporaryFileAccessorMock.Verify(
            a => a.DeleteIfExists(It.IsAny<string>()),
            Times.Never);
    }
}
```

- [ ] **Step 4: Run the test to verify it passes**

Run: `cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~ReprintExpeditionListHandlerTests"`
Expected: PASS, 5 tests, 0 failed.

- [ ] **Step 5: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/ExpeditionListArchive/UseCases/ReprintExpeditionList/ReprintExpeditionListHandler.cs \
        backend/src/Anela.Heblo.Application/Features/ExpeditionListArchive/ExpeditionListArchiveModule.cs \
        backend/test/Anela.Heblo.Tests/ExpeditionListArchive/ReprintExpeditionListHandlerTests.cs
git commit -m "refactor(expedition-list-archive): migrate ReprintExpeditionListHandler and its DI factory to IExpeditionListArchiveBlobStore"
```

---

