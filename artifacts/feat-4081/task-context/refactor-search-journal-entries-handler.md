### task: refactor-search-journal-entries-handler

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/Journal/UseCases/SearchJournalEntries/SearchJournalEntriesHandler.cs:36-45`
- Modify: `backend/test/Anela.Heblo.Tests/Features/Journal/SearchJournalEntriesHandlerTests.cs` (add pagination-metadata coverage — the existing 4 tests in this file never assert `TotalPages`/`HasNextPage`/`HasPreviousPage`)

- [ ] **Step 1: Write the failing test**

Add this test method inside the existing `SearchJournalEntriesHandlerTests` class in `backend/test/Anela.Heblo.Tests/Features/Journal/SearchJournalEntriesHandlerTests.cs` (insert before the closing `}` of the class, after `Handle_ReturnsRawContentFromEntry`):

```csharp
    [Fact]
    public async Task Handle_MiddlePage_ReturnsCorrectPaginationMetadata()
    {
        // Arrange
        var request = new SearchJournalEntriesRequest
        {
            PageNumber = 2,
            PageSize = 10
        };

        _repositoryMock
            .Setup(x => x.SearchEntriesAsync(
                It.IsAny<string?>(),
                It.IsAny<DateTime?>(),
                It.IsAny<DateTime?>(),
                It.IsAny<string?>(),
                It.IsAny<IReadOnlyCollection<int>?>(),
                It.IsAny<string?>(),
                It.IsAny<int>(),
                It.IsAny<int>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedResult<JournalEntry>
            {
                Items = new List<JournalEntry>(),
                TotalCount = 25,
                PageNumber = 2,
                PageSize = 10
            });

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        result.TotalCount.Should().Be(25);
        result.TotalPages.Should().Be(3);
        result.HasNextPage.Should().BeTrue();
        result.HasPreviousPage.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_FirstPageExactMultiple_HasNoNextOrPreviousPage()
    {
        // Arrange: TotalCount is an exact multiple of PageSize and this is the only/last page.
        var request = new SearchJournalEntriesRequest
        {
            PageNumber = 1,
            PageSize = 10
        };

        _repositoryMock
            .Setup(x => x.SearchEntriesAsync(
                It.IsAny<string?>(),
                It.IsAny<DateTime?>(),
                It.IsAny<DateTime?>(),
                It.IsAny<string?>(),
                It.IsAny<IReadOnlyCollection<int>?>(),
                It.IsAny<string?>(),
                It.IsAny<int>(),
                It.IsAny<int>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedResult<JournalEntry>
            {
                Items = new List<JournalEntry>(),
                TotalCount = 10,
                PageNumber = 1,
                PageSize = 10
            });

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        result.TotalPages.Should().Be(1);
        result.HasNextPage.Should().BeFalse();
        result.HasPreviousPage.Should().BeFalse();
    }
```

- [ ] **Step 2: Run test to verify it fails**

Run: `cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter FullyQualifiedName~SearchJournalEntriesHandlerTests`
Expected: as with the previous task, the handler already computes these values correctly inline today, so these two new tests PASS immediately — this locks in current behavior as a regression net before the refactor in Step 3.

- [ ] **Step 3: Refactor the handler to use the shared calculator**

Edit `backend/src/Anela.Heblo.Application/Features/Journal/UseCases/SearchJournalEntries/SearchJournalEntriesHandler.cs`:

```csharp
using Anela.Heblo.Application.Features.Journal.Contracts;
using Anela.Heblo.Application.Features.Journal.Mapping;
using Anela.Heblo.Application.Features.Journal.Pagination;
using Anela.Heblo.Domain.Features.Journal;
using MediatR;

namespace Anela.Heblo.Application.Features.Journal.UseCases.SearchJournalEntries
{
    public class SearchJournalEntriesHandler : IRequestHandler<SearchJournalEntriesRequest, SearchJournalEntriesResponse>
    {
        private readonly IJournalRepository _journalRepository;

        public SearchJournalEntriesHandler(IJournalRepository journalRepository)
        {
            _journalRepository = journalRepository;
        }

        public async Task<SearchJournalEntriesResponse> Handle(
            SearchJournalEntriesRequest request,
            CancellationToken cancellationToken)
        {
            var result = await _journalRepository.SearchEntriesAsync(
                searchText: request.SearchText,
                dateFrom: request.DateFrom,
                dateTo: request.DateTo,
                productCodePrefix: request.ProductCodePrefix,
                tagIds: request.TagIds,
                createdByUserId: request.CreatedByUserId,
                pageNumber: request.PageNumber,
                pageSize: request.PageSize,
                sortBy: request.SortBy,
                sortDirection: request.SortDirection,
                cancellationToken: cancellationToken);

            var entryDtos = result.Items.Select(JournalEntryMapper.ToDto).ToList();

            var (totalPages, hasNextPage, hasPreviousPage) =
                JournalPaginationCalculator.Calculate(result.TotalCount, request.PageNumber, request.PageSize);

            return new SearchJournalEntriesResponse
            {
                Entries = entryDtos,
                TotalCount = result.TotalCount,
                PageNumber = request.PageNumber,
                PageSize = request.PageSize,
                TotalPages = totalPages,
                HasNextPage = hasNextPage,
                HasPreviousPage = hasPreviousPage
            };
        }
    }
}
```

- [ ] **Step 4: Run test to verify it still passes**

Run: `cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter FullyQualifiedName~SearchJournalEntriesHandlerTests`
Expected: PASS — all 6 tests in the file green (the 4 pre-existing tests plus the 2 new pagination tests), proving the refactor produced byte-identical output to the old inline formula.

- [ ] **Step 5: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Journal/UseCases/SearchJournalEntries/SearchJournalEntriesHandler.cs backend/test/Anela.Heblo.Tests/Features/Journal/SearchJournalEntriesHandlerTests.cs
git commit -m "refactor(journal): SearchJournalEntriesHandler uses shared pagination calculator"
```

---
