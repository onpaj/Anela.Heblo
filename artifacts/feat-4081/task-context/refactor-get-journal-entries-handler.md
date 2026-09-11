### task: refactor-get-journal-entries-handler

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/Journal/UseCases/GetJournalEntries/GetJournalEntriesHandler.cs:30-39`
- Create: `backend/test/Anela.Heblo.Tests/Features/Journal/GetJournalEntriesHandlerTests.cs` (no test file exists for this handler today)

- [ ] **Step 1: Write the failing test**

Create `backend/test/Anela.Heblo.Tests/Features/Journal/GetJournalEntriesHandlerTests.cs`:

```csharp
using Anela.Heblo.Application.Features.Journal.Contracts;
using Anela.Heblo.Application.Features.Journal.UseCases.GetJournalEntries;
using Anela.Heblo.Domain.Features.Journal;
using Anela.Heblo.Xcc.Persistance;
using FluentAssertions;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Journal;

public class GetJournalEntriesHandlerTests
{
    private readonly Mock<IJournalRepository> _repositoryMock;
    private readonly GetJournalEntriesHandler _handler;

    public GetJournalEntriesHandlerTests()
    {
        _repositoryMock = new Mock<IJournalRepository>();
        _handler = new GetJournalEntriesHandler(_repositoryMock.Object);
    }

    [Fact]
    public async Task Handle_MiddlePage_ReturnsCorrectPaginationMetadata()
    {
        // Arrange
        var request = new GetJournalEntriesRequest
        {
            PageNumber = 2,
            PageSize = 10
        };

        var entry = new JournalEntry
        {
            Id = 1,
            Title = "Entry",
            Content = "Content",
            EntryDate = DateTime.Today,
            CreatedAt = DateTime.UtcNow,
            ModifiedAt = DateTime.UtcNow,
            CreatedByUserId = "user123"
        };

        _repositoryMock
            .Setup(x => x.GetEntriesAsync(
                It.IsAny<int>(),
                It.IsAny<int>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedResult<JournalEntry>
            {
                Items = new List<JournalEntry> { entry },
                TotalCount = 25,
                PageNumber = 2,
                PageSize = 10
            });

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        result.TotalCount.Should().Be(25);
        result.PageNumber.Should().Be(2);
        result.PageSize.Should().Be(10);
        result.TotalPages.Should().Be(3);
        result.HasNextPage.Should().BeTrue();
        result.HasPreviousPage.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_FirstPageExactMultiple_HasNoNextOrPreviousPage()
    {
        // Arrange: TotalCount is an exact multiple of PageSize and this is the only/last page.
        var request = new GetJournalEntriesRequest
        {
            PageNumber = 1,
            PageSize = 10
        };

        _repositoryMock
            .Setup(x => x.GetEntriesAsync(
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
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter FullyQualifiedName~GetJournalEntriesHandlerTests`
Expected: at this point the handler is unchanged (still has its own inline formula), so these tests actually already PASS against current behavior — this step is a safety net, not a red-then-green cycle, since the fix is a refactor of already-correct logic. Confirm they pass now, so any later regression is caught by these exact assertions before you touch the handler in Step 3.

- [ ] **Step 3: Refactor the handler to use the shared calculator**

Edit `backend/src/Anela.Heblo.Application/Features/Journal/UseCases/GetJournalEntries/GetJournalEntriesHandler.cs`. Add the using and replace the inline calculation:

```csharp
using Anela.Heblo.Application.Features.Journal.Contracts;
using Anela.Heblo.Application.Features.Journal.Mapping;
using Anela.Heblo.Application.Features.Journal.Pagination;
using Anela.Heblo.Domain.Features.Journal;
using MediatR;

namespace Anela.Heblo.Application.Features.Journal.UseCases.GetJournalEntries
{
    public class GetJournalEntriesHandler : IRequestHandler<GetJournalEntriesRequest, GetJournalEntriesResponse>
    {
        private readonly IJournalRepository _journalRepository;

        public GetJournalEntriesHandler(IJournalRepository journalRepository)
        {
            _journalRepository = journalRepository;
        }

        public async Task<GetJournalEntriesResponse> Handle(
            GetJournalEntriesRequest request,
            CancellationToken cancellationToken)
        {
            var result = await _journalRepository.GetEntriesAsync(
                request.PageNumber,
                request.PageSize,
                request.SortBy,
                request.SortDirection,
                cancellationToken);

            var entryDtos = result.Items.Select(JournalEntryMapper.ToDto).ToList();

            var (totalPages, hasNextPage, hasPreviousPage) =
                JournalPaginationCalculator.Calculate(result.TotalCount, request.PageNumber, request.PageSize);

            return new GetJournalEntriesResponse
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

Run: `cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter FullyQualifiedName~GetJournalEntriesHandlerTests`
Expected: PASS — both tests green, proving the refactor produced byte-identical output to the old inline formula.

- [ ] **Step 5: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Journal/UseCases/GetJournalEntries/GetJournalEntriesHandler.cs backend/test/Anela.Heblo.Tests/Features/Journal/GetJournalEntriesHandlerTests.cs
git commit -m "refactor(journal): GetJournalEntriesHandler uses shared pagination calculator"
```

---
