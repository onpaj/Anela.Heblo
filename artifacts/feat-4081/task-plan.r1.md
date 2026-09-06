# Journal Pagination Metadata Deduplication Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Eliminate the duplicated `TotalPages`/`HasNextPage`/`HasPreviousPage` calculation between `GetJournalEntriesHandler` and `SearchJournalEntriesHandler` by extracting it into a single shared, unit-tested helper in the Journal feature folder.

**Architecture:** Add one new `internal static class JournalPaginationCalculator` (in a new `Features/Journal/Pagination/` subfolder) exposing `Calculate(totalCount, pageNumber, pageSize)` → `(TotalPages, HasNextPage, HasPreviousPage)`. Both handlers call it instead of computing the three fields inline. No contract, DTO, endpoint, or behavior changes — this is a pure structural refactor confined to `Anela.Heblo.Application.Features.Journal`.

**Tech Stack:** .NET 8, xUnit, Moq, FluentAssertions (existing test stack — `backend/test/Anela.Heblo.Tests`).

---

### task: add-pagination-calculator

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/Journal/Pagination/JournalPaginationCalculator.cs`
- Create: `backend/test/Anela.Heblo.Tests/Features/Journal/JournalPaginationCalculatorTests.cs`

- [ ] **Step 1: Write the failing test**

Create `backend/test/Anela.Heblo.Tests/Features/Journal/JournalPaginationCalculatorTests.cs`:

```csharp
using Anela.Heblo.Application.Features.Journal.Pagination;
using FluentAssertions;
using Xunit;

namespace Anela.Heblo.Tests.Features.Journal;

public class JournalPaginationCalculatorTests
{
    [Theory]
    [InlineData(0, 1, 10, 0, false, false)]      // no rows at all
    [InlineData(5, 1, 10, 1, false, false)]       // single partial page
    [InlineData(10, 1, 10, 1, false, false)]      // exact multiple, on last page
    [InlineData(11, 1, 10, 2, true, false)]       // exact multiple + 1, more pages follow
    [InlineData(11, 2, 10, 2, false, true)]       // second (last) page of the above
    [InlineData(25, 2, 10, 3, true, true)]        // middle page
    [InlineData(25, 3, 10, 3, false, true)]       // last page, remainder row
    public void Calculate_ReturnsExpectedPaginationMetadata(
        int totalCount, int pageNumber, int pageSize,
        int expectedTotalPages, bool expectedHasNextPage, bool expectedHasPreviousPage)
    {
        // Act
        var result = JournalPaginationCalculator.Calculate(totalCount, pageNumber, pageSize);

        // Assert
        result.TotalPages.Should().Be(expectedTotalPages);
        result.HasNextPage.Should().Be(expectedHasNextPage);
        result.HasPreviousPage.Should().Be(expectedHasPreviousPage);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter FullyQualifiedName~JournalPaginationCalculatorTests`
Expected: build FAILS (CS0234 or similar) — `Anela.Heblo.Application.Features.Journal.Pagination` namespace / `JournalPaginationCalculator` type does not exist yet.

- [ ] **Step 3: Write minimal implementation**

Create `backend/src/Anela.Heblo.Application/Features/Journal/Pagination/JournalPaginationCalculator.cs`:

```csharp
using System;

namespace Anela.Heblo.Application.Features.Journal.Pagination
{
    /// <summary>
    /// Computes the pagination metadata (TotalPages, HasNextPage, HasPreviousPage) shared by
    /// GetJournalEntriesResponse and SearchJournalEntriesResponse. Extracted so the formula
    /// exists in exactly one place for both Journal list use cases.
    /// </summary>
    internal static class JournalPaginationCalculator
    {
        public static (int TotalPages, bool HasNextPage, bool HasPreviousPage) Calculate(
            int totalCount, int pageNumber, int pageSize)
        {
            var totalPages = (int)Math.Ceiling((double)totalCount / pageSize);
            return (
                TotalPages: totalPages,
                HasNextPage: pageNumber * pageSize < totalCount,
                HasPreviousPage: pageNumber > 1);
        }
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter FullyQualifiedName~JournalPaginationCalculatorTests`
Expected: PASS — all 7 theory cases green. (`internal` visibility resolves because `Anela.Heblo.Application/AssemblyInfo.cs` already declares `[assembly: InternalsVisibleTo("Anela.Heblo.Tests")]`.)

- [ ] **Step 5: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Journal/Pagination/JournalPaginationCalculator.cs backend/test/Anela.Heblo.Tests/Features/Journal/JournalPaginationCalculatorTests.cs
git commit -m "feat(journal): add shared pagination metadata calculator"
```

---

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

### task: full-suite-validation

**Files:**
- None modified — validation only.

- [ ] **Step 1: Run the full backend test suite**

Run: `cd backend && dotnet test`
Expected: PASS — no regressions anywhere in the solution (this refactor touches only the Journal module, but the full suite must still be green per repo convention).

- [ ] **Step 2: Build**

Run: `cd backend && dotnet build`
Expected: Build succeeds with no new warnings/errors.

- [ ] **Step 3: Format check**

Run: `cd backend && dotnet format --verify-no-changes`
Expected: No formatting violations. If it reports changes, run `dotnet format` and re-verify, then amend the affected task's commit or add a small follow-up formatting commit.

- [ ] **Step 4: Commit (if formatting fixed anything)**

```bash
git add -A
git commit -m "chore(journal): apply dotnet format" --allow-empty
```

(Skip this commit if `dotnet format --verify-no-changes` in Step 3 reported no changes.)

---

## Self-Review

**1. Spec coverage:**
- FR-1 (single shared calculation, both handlers use it, byte-identical output, lives in Journal folder, no cross-module change) → covered by `add-pagination-calculator`, `refactor-get-journal-entries-handler`, `refactor-search-journal-entries-handler`.
- FR-2 (no contract changes) → verified implicitly: no task modifies `Contracts/GetJournalEntriesRequest.cs`, `Contracts/SearchJournalEntriesResponse.cs`, `JournalModule.cs`, or any controller; the handler tests assert the same public response properties as before.
- NFR-2 (single source of truth for the formula) → satisfied by construction: after `add-pagination-calculator`, the formula's only implementation is `JournalPaginationCalculator.Calculate`.

**2. Placeholder scan:** No TBD/TODO, no "similar to Task N", no unresolved types — every code block above is complete and self-contained (each handler file is shown in full, not as a diff snippet).

**3. Type consistency:** `JournalPaginationCalculator.Calculate(int, int, int) -> (int TotalPages, bool HasNextPage, bool HasPreviousPage)` is defined once in `add-pagination-calculator` and consumed identically (same parameter order, same tuple deconstruction names) in both handler refactor tasks.
