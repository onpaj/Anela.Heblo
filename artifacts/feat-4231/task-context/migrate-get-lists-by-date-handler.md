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

