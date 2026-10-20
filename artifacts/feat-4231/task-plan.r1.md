# Decouple ExpeditionListArchive from FileStorage's IBlobStorageService Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the four `ExpeditionListArchive` handlers' direct dependency on FileStorage's `IBlobStorageService` with a narrow, consumer-owned `IExpeditionListArchiveBlobStore` contract, implemented by a new FileStorage-owned adapter, with zero behavioral change and a permanent regression guard.

**Architecture:** Consumer (`ExpeditionListArchive`) defines `IExpeditionListArchiveBlobStore` + `ExpeditionBlobItem` in its own `Contracts/` folder. Provider (`FileStorage`, Application layer) implements it via `ExpeditionListArchiveBlobStoreAdapter` in its `Infrastructure/` folder, delegating 1:1 to the existing `IBlobStorageService`, and registers the binding in `FileStorageModule.AddFileStorageModule`. This mirrors the existing `ILeafletKnowledgeSource` / `KnowledgeBaseLeafletSourceAdapter` pattern documented in `docs/architecture/development_guidelines.md`.

**Tech Stack:** .NET 8, MediatR, Moq + xUnit, FluentAssertions (for the architecture test).

---

### task: contracts-and-dto

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/ExpeditionListArchive/Contracts/IExpeditionListArchiveBlobStore.cs`
- Create: `backend/src/Anela.Heblo.Application/Features/ExpeditionListArchive/Contracts/ExpeditionBlobItem.cs`

- [ ] **Step 1: Create the `ExpeditionBlobItem` DTO**

```csharp
// backend/src/Anela.Heblo.Application/Features/ExpeditionListArchive/Contracts/ExpeditionBlobItem.cs
namespace Anela.Heblo.Application.Features.ExpeditionListArchive.Contracts;

/// <summary>
/// ExpeditionListArchive-owned metadata about a single archived expedition list blob.
/// Structurally mirrors Anela.Heblo.Domain.Features.FileStorage.BlobItemInfo, but is owned by
/// this module so ExpeditionListArchive never references the FileStorage domain directly.
/// </summary>
public class ExpeditionBlobItem
{
    public string Name { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public DateTimeOffset? CreatedOn { get; set; }
    public long? ContentLength { get; set; }
}
```

- [ ] **Step 2: Create the `IExpeditionListArchiveBlobStore` contract**

```csharp
// backend/src/Anela.Heblo.Application/Features/ExpeditionListArchive/Contracts/IExpeditionListArchiveBlobStore.cs
namespace Anela.Heblo.Application.Features.ExpeditionListArchive.Contracts;

/// <summary>
/// ExpeditionListArchive-owned narrow contract over blob storage. Exposes only the three
/// operations this module actually needs, implemented by a FileStorage-owned adapter
/// (see Anela.Heblo.Application.Features.FileStorage.Infrastructure.ExpeditionListArchiveBlobStoreAdapter).
/// Do not add speculative methods here — extend only when a handler in this module needs them.
/// </summary>
public interface IExpeditionListArchiveBlobStore
{
    Task<Stream> DownloadAsync(string containerName, string blobPath, CancellationToken cancellationToken);

    Task<IReadOnlyList<ExpeditionBlobItem>> ListBlobsAsync(string containerName, string? prefix, CancellationToken cancellationToken);

    Task<IReadOnlyList<string>> ListVirtualDirectoriesAsync(string containerName, CancellationToken cancellationToken);
}
```

- [ ] **Step 3: Build to confirm the new files compile**

Run: `cd backend && dotnet build src/Anela.Heblo.Application/Anela.Heblo.Application.csproj`
Expected: Build succeeded, 0 errors (the two new files have no dependents yet, so nothing else changes).

- [ ] **Step 4: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/ExpeditionListArchive/Contracts/IExpeditionListArchiveBlobStore.cs \
        backend/src/Anela.Heblo.Application/Features/ExpeditionListArchive/Contracts/ExpeditionBlobItem.cs
git commit -m "feat(expedition-list-archive): add IExpeditionListArchiveBlobStore contract and ExpeditionBlobItem DTO"
```

---

### task: adapter-and-di

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/FileStorage/Infrastructure/ExpeditionListArchiveBlobStoreAdapter.cs`
- Modify: `backend/src/Anela.Heblo.Application/Features/FileStorage/FileStorageModule.cs`

- [ ] **Step 1: Create the adapter**

```csharp
// backend/src/Anela.Heblo.Application/Features/FileStorage/Infrastructure/ExpeditionListArchiveBlobStoreAdapter.cs
using Anela.Heblo.Application.Features.ExpeditionListArchive.Contracts;
using Anela.Heblo.Domain.Features.FileStorage;

namespace Anela.Heblo.Application.Features.FileStorage.Infrastructure;

/// <summary>
/// FileStorage-owned adapter implementing ExpeditionListArchive's IExpeditionListArchiveBlobStore
/// contract by delegating to the existing IBlobStorageService. Pure delegation/mapping — no
/// business logic. Mirrors KnowledgeBaseLeafletSourceAdapter's shape and visibility.
/// </summary>
internal sealed class ExpeditionListArchiveBlobStoreAdapter : IExpeditionListArchiveBlobStore
{
    private readonly IBlobStorageService _blobStorageService;

    public ExpeditionListArchiveBlobStoreAdapter(IBlobStorageService blobStorageService)
    {
        _blobStorageService = blobStorageService;
    }

    public Task<Stream> DownloadAsync(string containerName, string blobPath, CancellationToken cancellationToken)
        => _blobStorageService.DownloadAsync(containerName, blobPath, cancellationToken);

    public async Task<IReadOnlyList<ExpeditionBlobItem>> ListBlobsAsync(string containerName, string? prefix, CancellationToken cancellationToken)
    {
        var blobs = await _blobStorageService.ListBlobsAsync(containerName, prefix, cancellationToken);
        return blobs
            .Select(b => new ExpeditionBlobItem
            {
                Name = b.Name,
                FileName = b.FileName,
                CreatedOn = b.CreatedOn,
                ContentLength = b.ContentLength,
            })
            .ToList();
    }

    public Task<IReadOnlyList<string>> ListVirtualDirectoriesAsync(string containerName, CancellationToken cancellationToken)
        => _blobStorageService.ListVirtualDirectoriesAsync(containerName, cancellationToken);
}
```

- [ ] **Step 2: Register the DI binding in `FileStorageModule`**

Modify `backend/src/Anela.Heblo.Application/Features/FileStorage/FileStorageModule.cs`. Add the using statement and the registration line just before the method's `return services;`:

```csharp
using Anela.Heblo.Application.Features.ExpeditionListArchive.Contracts;
```
(add alongside the existing `using Anela.Heblo.Application.Features.FileStorage.Infrastructure;` line at the top of the file)

```csharp
        services.AddScoped<IValidator<DownloadFromUrlRequest>, DownloadFromUrlRequestValidator>();
        services.AddScoped<IPipelineBehavior<DownloadFromUrlRequest, DownloadFromUrlResponse>,
            ValidationResultBehavior<DownloadFromUrlRequest, DownloadFromUrlResponse>>();

        // Provider-owned binding for ExpeditionListArchive's consumer contract (cross-module
        // communication pattern — see docs/architecture/development_guidelines.md). Singleton
        // to match the wrapped IBlobStorageService's own Singleton lifetime; the adapter holds
        // no state of its own.
        services.AddSingleton<IExpeditionListArchiveBlobStore, ExpeditionListArchiveBlobStoreAdapter>();

        return services;
```

- [ ] **Step 3: Build to confirm it compiles**

Run: `cd backend && dotnet build src/Anela.Heblo.Application/Anela.Heblo.Application.csproj`
Expected: Build succeeded, 0 errors.

- [ ] **Step 4: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/FileStorage/Infrastructure/ExpeditionListArchiveBlobStoreAdapter.cs \
        backend/src/Anela.Heblo.Application/Features/FileStorage/FileStorageModule.cs
git commit -m "feat(file-storage): add ExpeditionListArchiveBlobStoreAdapter and register DI binding"
```

---

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

### task: migrate-get-lists-by-date-handler

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/ExpeditionListArchive/UseCases/GetExpeditionListsByDate/GetExpeditionListsByDateHandler.cs`
- Modify: `backend/test/Anela.Heblo.Tests/ExpeditionListArchive/GetExpeditionListsByDateHandlerTests.cs`

- [ ] **Step 1: Update the handler**

Replace the full contents of `GetExpeditionListsByDateHandler.cs`:

```csharp
using Anela.Heblo.Application.Features.ExpeditionListArchive.Contracts;
using Anela.Heblo.Application.Shared;
using MediatR;
using Microsoft.Extensions.Options;

namespace Anela.Heblo.Application.Features.ExpeditionListArchive.UseCases.GetExpeditionListsByDate;

public class GetExpeditionListsByDateHandler : IRequestHandler<GetExpeditionListsByDateRequest, GetExpeditionListsByDateResponse>
{
    private readonly IExpeditionListArchiveBlobStore _blobStore;
    private readonly string _containerName;

    public GetExpeditionListsByDateHandler(IExpeditionListArchiveBlobStore blobStore, IOptions<ExpeditionListArchiveOptions> options)
    {
        _blobStore = blobStore;
        _containerName = options.Value.BlobContainerName;
    }

    public async Task<GetExpeditionListsByDateResponse> Handle(GetExpeditionListsByDateRequest request, CancellationToken cancellationToken)
    {
        if (!DateOnly.TryParseExact(request.Date, "yyyy-MM-dd", out _))
        {
            return new GetExpeditionListsByDateResponse
            {
                Success = false,
                ErrorCode = ErrorCodes.InvalidFormat,
                Params = new Dictionary<string, string>
                {
                    { "Field", "Date" },
                    { "ExpectedFormat", "yyyy-MM-dd" }
                }
            };
        }

        var blobs = await _blobStore.ListBlobsAsync(_containerName, request.Date, cancellationToken);

        var items = blobs
            .Where(b => b.FileName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
            .Select(b => new ExpeditionListItemDto
            {
                BlobPath = b.Name,
                FileName = b.FileName,
                ListId = Path.GetFileNameWithoutExtension(b.FileName),
                CreatedOn = b.CreatedOn,
                ContentLength = b.ContentLength
            })
            .ToList();

        return new GetExpeditionListsByDateResponse { Items = items };
    }
}
```

Note: `using Anela.Heblo.Domain.Features.FileStorage;` is removed; the `blobs` variable is now `IReadOnlyList<ExpeditionBlobItem>` instead of `IReadOnlyList<BlobItemInfo>` (inferred, no explicit type change needed in source), and the `.Select(b => ...)` projection body is untouched since `ExpeditionBlobItem` and `BlobItemInfo` have identical property names/types.

- [ ] **Step 2: Update the test**

Replace the full contents of `GetExpeditionListsByDateHandlerTests.cs`:

```csharp
using Anela.Heblo.Application.Features.ExpeditionListArchive;
using Anela.Heblo.Application.Features.ExpeditionListArchive.Contracts;
using Anela.Heblo.Application.Features.ExpeditionListArchive.UseCases.GetExpeditionListsByDate;
using Anela.Heblo.Application.Shared;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.ExpeditionListArchive;

public class GetExpeditionListsByDateHandlerTests
{
    private readonly Mock<IExpeditionListArchiveBlobStore> _blobStoreMock;
    private readonly GetExpeditionListsByDateHandler _handler;
    private const string ContainerName = "expedition-lists";

    public GetExpeditionListsByDateHandlerTests()
    {
        _blobStoreMock = new Mock<IExpeditionListArchiveBlobStore>();
        _handler = new GetExpeditionListsByDateHandler(_blobStoreMock.Object, Options.Create(new ExpeditionListArchiveOptions()));
    }

    [Fact]
    public async Task Handle_ReturnsItemsForDate()
    {
        // Arrange
        var date = "2026-03-25";
        var blobs = new List<ExpeditionBlobItem>
        {
            new() { Name = $"{date}/picking-list-001.pdf", FileName = "picking-list-001.pdf", CreatedOn = new DateTimeOffset(2026, 3, 25, 10, 0, 0, TimeSpan.Zero), ContentLength = 512000 },
            new() { Name = $"{date}/picking-list-002.pdf", FileName = "picking-list-002.pdf", CreatedOn = new DateTimeOffset(2026, 3, 25, 14, 0, 0, TimeSpan.Zero), ContentLength = 256000 },
        };

        _blobStoreMock
            .Setup(s => s.ListBlobsAsync(ContainerName, date, default))
            .ReturnsAsync(blobs.AsReadOnly());

        var request = new GetExpeditionListsByDateRequest { Date = date };

        // Act
        var result = await _handler.Handle(request, default);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(2, result.Items.Count);
        Assert.Equal("picking-list-001.pdf", result.Items[0].FileName);
        Assert.Equal($"{date}/picking-list-001.pdf", result.Items[0].BlobPath);
        Assert.Equal(512000, result.Items[0].ContentLength);
        Assert.Equal("picking-list-001", result.Items[0].ListId);
        Assert.Equal("picking-list-002", result.Items[1].ListId);
    }

    [Fact]
    public async Task Handle_FiltersPdfFilesOnly()
    {
        // Arrange
        var date = "2026-03-25";
        var blobs = new List<ExpeditionBlobItem>
        {
            new() { Name = $"{date}/picking-list-001.pdf", FileName = "picking-list-001.pdf" },
            new() { Name = $"{date}/picking-list-002.txt", FileName = "picking-list-002.txt" },
        };

        _blobStoreMock
            .Setup(s => s.ListBlobsAsync(ContainerName, date, default))
            .ReturnsAsync(blobs.AsReadOnly());

        var request = new GetExpeditionListsByDateRequest { Date = date };

        // Act
        var result = await _handler.Handle(request, default);

        // Assert
        Assert.Single(result.Items);
        Assert.Equal("picking-list-001.pdf", result.Items[0].FileName);
    }

    [Theory]
    [InlineData("not-a-date")]
    [InlineData("2026/03/25")]
    [InlineData("25-03-2026")]
    [InlineData("")]
    [InlineData(null)]
    public async Task Handle_ReturnsFailure_WhenDateIsInvalid(string? invalidDate)
    {
        // Arrange
        var request = new GetExpeditionListsByDateRequest { Date = invalidDate ?? string.Empty };

        // Act
        var result = await _handler.Handle(request, default);

        // Assert
        Assert.NotNull(result);
        Assert.False(result.Success);
        Assert.Equal(ErrorCodes.InvalidFormat, result.ErrorCode);
        Assert.NotNull(result.Params);
        Assert.Equal("Date", result.Params!["Field"]);
        Assert.Equal("yyyy-MM-dd", result.Params!["ExpectedFormat"]);
        Assert.Empty(result.Items);

        _blobStoreMock.Verify(
            s => s.ListBlobsAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }
}
```

- [ ] **Step 3: Run the test to verify it passes**

Run: `cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~GetExpeditionListsByDateHandlerTests"`
Expected: PASS, 3 tests (7 cases with Theory), 0 failed.

- [ ] **Step 4: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/ExpeditionListArchive/UseCases/GetExpeditionListsByDate/GetExpeditionListsByDateHandler.cs \
        backend/test/Anela.Heblo.Tests/ExpeditionListArchive/GetExpeditionListsByDateHandlerTests.cs
git commit -m "refactor(expedition-list-archive): migrate GetExpeditionListsByDateHandler to IExpeditionListArchiveBlobStore"
```

---

### task: migrate-get-dates-handler

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/ExpeditionListArchive/UseCases/GetExpeditionDates/GetExpeditionDatesHandler.cs`
- Modify: `backend/test/Anela.Heblo.Tests/ExpeditionListArchive/GetExpeditionDatesHandlerTests.cs`

- [ ] **Step 1: Update the handler**

Replace the full contents of `GetExpeditionDatesHandler.cs`:

```csharp
using Anela.Heblo.Application.Features.ExpeditionListArchive.Contracts;
using MediatR;
using Microsoft.Extensions.Options;

namespace Anela.Heblo.Application.Features.ExpeditionListArchive.UseCases.GetExpeditionDates;

public class GetExpeditionDatesHandler : IRequestHandler<GetExpeditionDatesRequest, GetExpeditionDatesResponse>
{
    private readonly IExpeditionListArchiveBlobStore _blobStore;
    private readonly string _containerName;

    public GetExpeditionDatesHandler(IExpeditionListArchiveBlobStore blobStore, IOptions<ExpeditionListArchiveOptions> options)
    {
        _blobStore = blobStore;
        _containerName = options.Value.BlobContainerName;
    }

    public async Task<GetExpeditionDatesResponse> Handle(GetExpeditionDatesRequest request, CancellationToken cancellationToken)
    {
        var prefixes = await _blobStore.ListVirtualDirectoriesAsync(_containerName, cancellationToken);

        var dates = prefixes
            .Where(IsValidDatePrefix)
            .OrderByDescending(d => d, StringComparer.Ordinal)
            .ToList();

        var totalCount = dates.Count;
        var pagedDates = dates
            .Skip((request.Page - 1) * request.PageSize)
            .Take(request.PageSize)
            .ToList();

        return new GetExpeditionDatesResponse
        {
            Dates = pagedDates,
            TotalCount = totalCount,
            Page = request.Page,
            PageSize = request.PageSize
        };
    }

    private static bool IsValidDatePrefix(string prefix)
    {
        return DateOnly.TryParseExact(prefix, "yyyy-MM-dd", out _);
    }
}
```

Note: `using Anela.Heblo.Domain.Features.FileStorage;` is removed; only the field/parameter type and name change, plus the call site. All filtering/sorting/pagination logic is unchanged.

- [ ] **Step 2: Update the test**

Replace the full contents of `GetExpeditionDatesHandlerTests.cs`:

```csharp
using Anela.Heblo.Application.Features.ExpeditionListArchive;
using Anela.Heblo.Application.Features.ExpeditionListArchive.Contracts;
using Anela.Heblo.Application.Features.ExpeditionListArchive.UseCases.GetExpeditionDates;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.ExpeditionListArchive;

public class GetExpeditionDatesHandlerTests
{
    private readonly Mock<IExpeditionListArchiveBlobStore> _blobStoreMock;
    private readonly GetExpeditionDatesHandler _handler;
    private const string ContainerName = "expedition-lists";

    public GetExpeditionDatesHandlerTests()
    {
        _blobStoreMock = new Mock<IExpeditionListArchiveBlobStore>();
        _handler = new GetExpeditionDatesHandler(_blobStoreMock.Object, Options.Create(new ExpeditionListArchiveOptions()));
    }

    [Fact]
    public async Task Handle_ReturnsDatesSortedDescending()
    {
        // Arrange
        var prefixes = new List<string> { "2026-03-24", "2026-03-25", "2026-03-23" };
        _blobStoreMock
            .Setup(s => s.ListVirtualDirectoriesAsync(ContainerName, It.IsAny<CancellationToken>()))
            .ReturnsAsync(prefixes.AsReadOnly());

        var request = new GetExpeditionDatesRequest { Page = 1, PageSize = 20 };

        // Act
        var result = await _handler.Handle(request, default);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(3, result.TotalCount);
        Assert.Equal(3, result.Dates.Count);
        Assert.Equal("2026-03-25", result.Dates[0]);
        Assert.Equal("2026-03-24", result.Dates[1]);
        Assert.Equal("2026-03-23", result.Dates[2]);
    }

    [Fact]
    public async Task Handle_PaginatesCorrectly()
    {
        // Arrange
        var prefixes = new List<string>();
        for (int i = 1; i <= 25; i++)
        {
            prefixes.Add($"2026-01-{i:D2}");
        }

        _blobStoreMock
            .Setup(s => s.ListVirtualDirectoriesAsync(ContainerName, It.IsAny<CancellationToken>()))
            .ReturnsAsync(prefixes.AsReadOnly());

        var request = new GetExpeditionDatesRequest { Page = 2, PageSize = 20 };

        // Act
        var result = await _handler.Handle(request, default);

        // Assert
        Assert.Equal(25, result.TotalCount);
        Assert.Equal(5, result.Dates.Count); // page 2 of 20: items 21-25
    }

    [Fact]
    public async Task Handle_EmptyContainer_ReturnsEmptyList()
    {
        // Arrange
        _blobStoreMock
            .Setup(s => s.ListVirtualDirectoriesAsync(ContainerName, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string>().AsReadOnly());

        var request = new GetExpeditionDatesRequest { Page = 1, PageSize = 20 };

        // Act
        var result = await _handler.Handle(request, default);

        // Assert
        Assert.Equal(0, result.TotalCount);
        Assert.Empty(result.Dates);
    }

    [Fact]
    public async Task Handle_CallsListVirtualDirectoriesOnce_AndNeverCallsListBlobs()
    {
        // Arrange
        var prefixes = new List<string> { "2026-03-25", "2026-03-24" };
        _blobStoreMock
            .Setup(s => s.ListVirtualDirectoriesAsync(ContainerName, It.IsAny<CancellationToken>()))
            .ReturnsAsync(prefixes.AsReadOnly());

        var request = new GetExpeditionDatesRequest { Page = 1, PageSize = 20 };

        // Act
        await _handler.Handle(request, default);

        // Assert
        _blobStoreMock.Verify(
            s => s.ListVirtualDirectoriesAsync(ContainerName, It.IsAny<CancellationToken>()),
            Times.Once);
        _blobStoreMock.Verify(
            s => s.ListBlobsAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_FiltersOutNonDatePrefixes()
    {
        // Arrange — mix of valid dates, structurally wrong, semantically wrong, and a sentinel folder.
        var prefixes = new List<string>
        {
            "2026-03-25",       // valid
            "miscellaneous",    // not a date
            "2026-13-99",       // structurally yyyy-MM-dd but invalid month/day
            "2026-03-24",       // valid
            "not-a-date",       // not a date
            "2025-12-31"        // valid
        };
        _blobStoreMock
            .Setup(s => s.ListVirtualDirectoriesAsync(ContainerName, It.IsAny<CancellationToken>()))
            .ReturnsAsync(prefixes.AsReadOnly());

        var request = new GetExpeditionDatesRequest { Page = 1, PageSize = 20 };

        // Act
        var result = await _handler.Handle(request, default);

        // Assert — only the three valid dates remain, in descending ordinal order.
        Assert.Equal(3, result.TotalCount);
        Assert.Equal(new[] { "2026-03-25", "2026-03-24", "2025-12-31" }, result.Dates);
    }
}
```

- [ ] **Step 3: Run the test to verify it passes**

Run: `cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~GetExpeditionDatesHandlerTests"`
Expected: PASS, 5 tests, 0 failed.

- [ ] **Step 4: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/ExpeditionListArchive/UseCases/GetExpeditionDates/GetExpeditionDatesHandler.cs \
        backend/test/Anela.Heblo.Tests/ExpeditionListArchive/GetExpeditionDatesHandlerTests.cs
git commit -m "refactor(expedition-list-archive): migrate GetExpeditionDatesHandler to IExpeditionListArchiveBlobStore"
```

---

### task: module-boundary-guard

**Files:**
- Modify: `backend/test/Anela.Heblo.Tests/Architecture/ModuleBoundariesTests.cs`

- [ ] **Step 1: Add the new allowlist field**

Find this line near the top of the class (right after the `LeafletAllowlist` declaration, alphabetically/logically grouped with the other per-pair allowlist fields — insert near the other `ExpeditionList*` allowlists, e.g. right after `ExpeditionListShoptetOrdersAllowlist`):

```csharp
    // Allowlist for ExpeditionListArchive -> FileStorage. Empty — the four ExpeditionListArchive
    // handlers now consume the module-owned IExpeditionListArchiveBlobStore contract; the
    // FileStorage adapter (ExpeditionListArchiveBlobStoreAdapter) lives in FileStorage.Infrastructure
    // and implements it there, so no ExpeditionListArchive type needs to reference FileStorage directly.
    private static readonly HashSet<string> ExpeditionListArchiveFileStorageAllowlist = new(StringComparer.Ordinal);
```

- [ ] **Step 2: Add the new rule to `Rules()`**

Find the existing `"ExpeditionListArchive -> ExpeditionList"` rule entry inside the `Rules()` `TheoryData` initializer and add a new entry immediately after it:

```csharp
        new ModuleBoundaryRule(
            Name: "ExpeditionListArchive -> ExpeditionList",
            InspectedNamespacePrefix: "Anela.Heblo.Application.Features.ExpeditionListArchive",
            ForbiddenNamespacePrefixes: new[]
            {
                "Anela.Heblo.Domain.Features.ExpeditionList",
                "Anela.Heblo.Application.Features.ExpeditionList",
                "Anela.Heblo.Persistence.ExpeditionList",
            },
            Allowlist: new HashSet<string>(StringComparer.Ordinal)),

        new ModuleBoundaryRule(
            Name: "ExpeditionListArchive -> FileStorage",
            InspectedNamespacePrefix: "Anela.Heblo.Application.Features.ExpeditionListArchive",
            ForbiddenNamespacePrefixes: new[]
            {
                "Anela.Heblo.Domain.Features.FileStorage",
                "Anela.Heblo.Application.Features.FileStorage",
                "Anela.Heblo.Persistence.FileStorage",
            },
            Allowlist: ExpeditionListArchiveFileStorageAllowlist),
```

(The first block, `"ExpeditionListArchive -> ExpeditionList"`, is unchanged and shown only for exact insertion-point context — do not duplicate it.)

- [ ] **Step 3: Run the architecture test to verify it passes**

Run: `cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~ModuleBoundariesTests"`
Expected: PASS, all rules in the `Rules()` theory pass with zero violations, including the new `"ExpeditionListArchive -> FileStorage"` row. If this fails, the failure message lists every remaining `ExpeditionListArchive → FileStorage` reference by name — cross-check that all four tasks above (`migrate-download-handler`, `migrate-reprint-handler`, `migrate-get-lists-by-date-handler`, `migrate-get-dates-handler`) were completed and committed first.

- [ ] **Step 4: Commit**

```bash
git add backend/test/Anela.Heblo.Tests/Architecture/ModuleBoundariesTests.cs
git commit -m "test(architecture): guard ExpeditionListArchive -> FileStorage module boundary"
```

---

### task: final-verification

**Files:** none (verification only)

- [ ] **Step 1: Full backend build**

Run: `cd backend && dotnet build`
Expected: Build succeeded, 0 errors, 0 new warnings.

- [ ] **Step 2: Format check**

Run: `cd backend && dotnet format --verify-no-changes`
Expected: No formatting changes required. If it reports changes, run `dotnet format` (without `--verify-no-changes`) and re-run Step 1.

- [ ] **Step 3: Full ExpeditionListArchive + Architecture test suite**

Run: `cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~ExpeditionListArchive|FullyQualifiedName~ModuleBoundariesTests"`
Expected: PASS, 0 failed (covers all 4 migrated handler test files, `BlobPathValidatorTests.cs` (unchanged, no dependency on `IBlobStorageService`), and the architecture guard).

- [ ] **Step 4: Full backend test suite (regression check)**

Run: `cd backend && dotnet test`
Expected: PASS, 0 failed. In particular, confirm `FileStorage` module's own tests (`AzureBlobStorageServiceTests.cs`, `DownloadFromUrlHandlerTests.cs`, `MockBlobStorageService.cs`, `Pipeline/FileStorageValidationPipelineTests.cs`, `AzureAdapterModuleTests.cs`) are unaffected — they mock/test `IBlobStorageService` directly and this refactor does not touch that interface or its other consumers.

- [ ] **Step 5: Verify no stray references remain**

Run: `cd backend && grep -rl "IBlobStorageService\|BlobItemInfo" src/Anela.Heblo.Application/Features/ExpeditionListArchive/`
Expected: No output (empty result) — zero files under `ExpeditionListArchive` reference `IBlobStorageService` or `BlobItemInfo` any more. If any file is listed, it was missed by an earlier task — go back and fix it before proceeding.

- [ ] **Step 6: Final commit (only if Step 2's `dotnet format` produced changes)**

```bash
git add -A backend
git commit -m "chore(expedition-list-archive): apply dotnet format after IExpeditionListArchiveBlobStore migration"
```

If Step 2 required no changes, skip this commit — there is nothing to commit.

---

## Self-Review

**1. Spec coverage:**
- FR-1 (narrow contract) → `contracts-and-dto` Step 2.
- FR-2 (`ExpeditionBlobItem` DTO) → `contracts-and-dto` Step 1.
- FR-3 (adapter) → `adapter-and-di` Step 1.
- FR-4 (DI registration) → `adapter-and-di` Step 2.
- FR-5 (migrate all four handlers, including the `ReprintExpeditionListHandler` manual factory) → `migrate-download-handler`, `migrate-reprint-handler` (Steps 1–2 cover both the handler and the factory), `migrate-get-lists-by-date-handler`, `migrate-get-dates-handler`.
- FR-6 (update existing unit tests) → each `migrate-*` task's Step 2/3 (test file rewrite), plus `final-verification` Step 4 confirms `FileStorage`-owned tests are untouched.
- FR-7 (module-boundary guard) → `module-boundary-guard`.
- NFR-1 (zero behavioral change) → every handler rewrite explicitly preserves the `Handle()` body; `final-verification` Step 4 is the regression check.
- NFR-2 (no API/contract surface change) → no `*Request`/`*Response`/`ExpeditionListItemDto` file is touched anywhere in this plan.
- NFR-3 / arch-review Decision 3 (Singleton lifetime) → `adapter-and-di` Step 2 registers `AddSingleton`.
- Arch-review Decisions 1–4 (adapter placement, binding site, lifetime, factory update) → all directly reflected in `adapter-and-di` and `migrate-reprint-handler`.

**2. Placeholder scan:** No TBD/TODO/"add appropriate"/"similar to Task N" patterns — every step shows full file contents or exact diffs.

**3. Type consistency:** `IExpeditionListArchiveBlobStore` and `ExpeditionBlobItem` signatures are identical across `contracts-and-dto`, `adapter-and-di`, and all four `migrate-*` tasks. Field name `_blobStore` (not `_blobStorageService` or `_store`) is used consistently in every handler and its test's mock variable name `_blobStoreMock`. Adapter class name `ExpeditionListArchiveBlobStoreAdapter` and namespace `Anela.Heblo.Application.Features.FileStorage.Infrastructure` are consistent between `adapter-and-di` and the `module-boundary-guard` allowlist comment.
