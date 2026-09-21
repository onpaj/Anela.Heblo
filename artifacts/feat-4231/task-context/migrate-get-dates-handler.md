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

