# Split `JournalEntryDto` into List/Detail and Search Variants — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Extract a search-only `SearchJournalEntryDto` so `ContentPreview` / `HighlightedTerms` no longer leak into list/detail responses, and so search hits no longer ship the full `Content` (≤10 KB per row) over the wire.

**Architecture:** Two flat DTOs (no inheritance, no `allOf`): `JournalEntryDto` (list/detail/catalog widget, carries full `Content`) and `SearchJournalEntryDto` (search hits, carries `ContentPreview` + `HighlightedTerms`, **no** `Content`). Search handler uses a new `JournalEntryMapper.ToSearchDto(JournalEntry)` that never copies `Content`; preview is computed in the handler from the domain `entry.Content`. Frontend `JournalList` consumes one of two distinct generated types and renders rows in two type-narrowed branches instead of a runtime check on a nullable field.

**Tech Stack:** .NET 8 (xUnit, FluentAssertions, Moq), NSwag (auto-generates `frontend/src/api/generated/api-client.ts` on `dotnet build` of the API in Debug), React 18 / TypeScript 4.9 (Create React App), React Query.

**Repository note:** Work happens in the worktree at `/Users/pajgrtondrej/Work/GitHub/Anela.Heblo/.worktrees/feat-arch-review-journal-journalentrydto-carr`. All paths in this plan are relative to that worktree root. Use `cd` once at session start; do not prepend it to every command.

---

## File Map

**Create**
- `backend/src/Anela.Heblo.Application/Features/Journal/Contracts/SearchJournalEntryDto.cs` — new flat DTO class for search hits.

**Modify (backend)**
- `backend/src/Anela.Heblo.Application/Features/Journal/Contracts/JournalEntryDto.cs` — drop `ContentPreview` and `HighlightedTerms`.
- `backend/src/Anela.Heblo.Application/Features/Journal/Contracts/SearchJournalEntriesResponse.cs` — `Entries` type changes to `List<SearchJournalEntryDto>`.
- `backend/src/Anela.Heblo.Application/Features/Journal/Mapping/JournalEntryMapper.cs` — add `ToSearchDto(JournalEntry)` (no `Content` copy); keep `ToDto` unchanged.
- `backend/src/Anela.Heblo.Application/Features/Journal/UseCases/SearchJournalEntries/SearchJournalEntriesHandler.cs` — switch projection to `ToSearchDto`; always populate `ContentPreview` from `entry.Content`; populate `HighlightedTerms` only when search text is non-empty.

**Modify (backend tests)**
- `backend/test/Anela.Heblo.Tests/Features/Journal/JournalEntryMapperTests.cs` — remove the two tests that asserted search fields on `JournalEntryDto`; add three `ToSearchDto` tests.
- `backend/test/Anela.Heblo.Tests/Features/Journal/SearchJournalEntriesHandlerTests.cs` — assert against `SearchJournalEntryDto`, add three new tests (preview window, empty-search fallback, highlight terms filter).

**Modify (frontend)**
- `frontend/src/api/generated/api-client.ts` — **auto-regenerated** by NSwag during `dotnet build` of `Anela.Heblo.API` in Debug. Do not hand-edit. Verify after rebuild.
- `frontend/src/components/pages/Journal/JournalList.tsx` — replace the `isSearchMode && entry.contentPreview` branch with a typed two-branch render keyed off `isSearchMode`.

**Leave unchanged**
- `backend/src/Anela.Heblo.API/Controllers/JournalController.cs` (forwards MediatR result by type).
- `frontend/src/api/hooks/useJournal.ts` (no return-type annotations to update).
- `frontend/src/components/JournalEntryForm.tsx`, `JournalEntryModal.tsx`, `catalog/detail/**` (consume `JournalEntryDto` but never touch the removed fields — verified via grep).

---

## Task 1: Create `SearchJournalEntryDto`

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/Journal/Contracts/SearchJournalEntryDto.cs`

- [ ] **Step 1: Create the DTO class**

Write the file with exactly this content (DTO must be a `class`, not `record` — see CLAUDE.md):

```csharp
using System;
using System.Collections.Generic;

namespace Anela.Heblo.Application.Features.Journal.Contracts
{
    public class SearchJournalEntryDto
    {
        public int Id { get; set; }
        public string? Title { get; set; }
        public DateTime EntryDate { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime ModifiedAt { get; set; }
        public string CreatedByUserId { get; set; } = null!;
        public string? CreatedByUsername { get; set; }
        public string? ModifiedByUserId { get; set; }
        public string? ModifiedByUsername { get; set; }

        public List<string> AssociatedProducts { get; set; } = new();
        public List<JournalEntryTagDto> Tags { get; set; } = new();

        public string ContentPreview { get; set; } = string.Empty;
        public List<string> HighlightedTerms { get; set; } = new();
    }
}
```

Notes for the engineer:
- `ContentPreview` is initialised to `string.Empty` (not `null!`) so a half-built DTO never accidentally ships a null in a non-nullable contract field — see arch-review FR-4 amendment.
- No `Content` field. That is the whole point of this DTO.
- `JournalEntryTagDto` already lives in `Contracts/JournalEntryDto.cs` in the same namespace; do **not** redeclare it.

- [ ] **Step 2: Verify the project still builds**

Run: `dotnet build backend/src/Anela.Heblo.Application/Anela.Heblo.Application.csproj`
Expected: Build succeeded, 0 errors. (Nothing consumes the new type yet.)

- [ ] **Step 3: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Journal/Contracts/SearchJournalEntryDto.cs
git commit -m "feat(journal): add SearchJournalEntryDto contract"
```

---

## Task 2: Add `ToSearchDto` mapper (TDD)

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/Journal/Mapping/JournalEntryMapper.cs`
- Modify: `backend/test/Anela.Heblo.Tests/Features/Journal/JournalEntryMapperTests.cs`

- [ ] **Step 1: Write failing tests for `ToSearchDto`**

Append the following three tests **inside** the `JournalEntryMapperTests` class in `backend/test/Anela.Heblo.Tests/Features/Journal/JournalEntryMapperTests.cs`, just before the closing `}` of the class (after the existing `ToDto_HighlightedTerms_IsEmptyAfterMapping` test):

```csharp
    [Fact]
    public void ToSearchDto_MapsAllScalarFields_AndOmitsContent()
    {
        // Arrange
        var entry = BuildFullEntry();

        // Act
        var dto = JournalEntryMapper.ToSearchDto(entry);

        // Assert
        dto.Id.Should().Be(42);
        dto.Title.Should().Be("Test Entry");
        dto.EntryDate.Should().Be(new DateTime(2025, 1, 15));
        dto.CreatedAt.Should().Be(new DateTime(2025, 1, 15, 10, 0, 0));
        dto.ModifiedAt.Should().Be(new DateTime(2025, 1, 15, 11, 0, 0));
        dto.CreatedByUserId.Should().Be("user-001");
        dto.CreatedByUsername.Should().Be("alice");
        dto.ModifiedByUserId.Should().Be("user-002");
        dto.ModifiedByUsername.Should().Be("bob");
        // SearchJournalEntryDto intentionally has no Content field;
        // the absence of a property is the assertion (compile-time guarantee).
    }

    [Fact]
    public void ToSearchDto_AssociatedProductsAndTags_AreMappedSameAsToDto()
    {
        // Arrange
        var entry = BuildFullEntry();

        // Act
        var dto = JournalEntryMapper.ToSearchDto(entry);

        // Assert
        dto.AssociatedProducts.Should().BeEquivalentTo(new[] { "TON001", "AKL002" });
        dto.Tags.Should().HaveCount(2);
        dto.Tags.Select(t => t.Id).Should().BeEquivalentTo(new[] { 10, 20 });
    }

    [Fact]
    public void ToSearchDto_LeavesContentPreviewEmpty_AndHighlightedTermsEmpty()
    {
        // Arrange
        var entry = BuildFullEntry();

        // Act
        var dto = JournalEntryMapper.ToSearchDto(entry);

        // Assert
        // Defaults set by the mapper; the handler is responsible for populating these.
        dto.ContentPreview.Should().Be(string.Empty);
        dto.HighlightedTerms.Should().NotBeNull().And.BeEmpty();
    }
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~JournalEntryMapperTests" --no-restore`
Expected: Build FAILS with `JournalEntryMapper` does not contain a definition for `ToSearchDto` (three errors).

- [ ] **Step 3: Implement `ToSearchDto`**

Edit `backend/src/Anela.Heblo.Application/Features/Journal/Mapping/JournalEntryMapper.cs`. Replace the entire file content with:

```csharp
using Anela.Heblo.Application.Features.Journal.Contracts;
using Anela.Heblo.Domain.Features.Journal;

namespace Anela.Heblo.Application.Features.Journal.Mapping
{
    internal static class JournalEntryMapper
    {
        public static JournalEntryDto ToDto(JournalEntry entry)
        {
            return new JournalEntryDto
            {
                Id = entry.Id,
                Title = entry.Title,
                Content = entry.Content,
                EntryDate = entry.EntryDate,
                CreatedAt = entry.CreatedAt,
                ModifiedAt = entry.ModifiedAt,
                CreatedByUserId = entry.CreatedByUserId,
                CreatedByUsername = entry.CreatedByUsername,
                ModifiedByUserId = entry.ModifiedByUserId,
                ModifiedByUsername = entry.ModifiedByUsername,
                AssociatedProducts = entry.ProductAssociations
                    .Select(pa => pa.ProductCodePrefix)
                    .Distinct()
                    .ToList(),
                Tags = entry.TagAssignments
                    .Where(ta => ta.Tag != null)
                    .Select(ta => new JournalEntryTagDto
                    {
                        Id = ta.Tag.Id,
                        Name = ta.Tag.Name,
                        Color = ta.Tag.Color
                    })
                    .ToList()
            };
        }

        public static SearchJournalEntryDto ToSearchDto(JournalEntry entry)
        {
            return new SearchJournalEntryDto
            {
                Id = entry.Id,
                Title = entry.Title,
                EntryDate = entry.EntryDate,
                CreatedAt = entry.CreatedAt,
                ModifiedAt = entry.ModifiedAt,
                CreatedByUserId = entry.CreatedByUserId,
                CreatedByUsername = entry.CreatedByUsername,
                ModifiedByUserId = entry.ModifiedByUserId,
                ModifiedByUsername = entry.ModifiedByUsername,
                AssociatedProducts = entry.ProductAssociations
                    .Select(pa => pa.ProductCodePrefix)
                    .Distinct()
                    .ToList(),
                Tags = entry.TagAssignments
                    .Where(ta => ta.Tag != null)
                    .Select(ta => new JournalEntryTagDto
                    {
                        Id = ta.Tag.Id,
                        Name = ta.Tag.Name,
                        Color = ta.Tag.Color
                    })
                    .ToList()
                // ContentPreview defaults to string.Empty; HighlightedTerms defaults to new List<string>().
                // The search handler overwrites these with real values before returning.
            };
        }
    }
}
```

Note: The two projections share their `AssociatedProducts` and `Tags` mapping shapes. Extracting a helper here would be premature (DRY only kicks in once we have a real third caller — YAGNI). Inline duplication is acceptable for two static factory methods.

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~JournalEntryMapperTests"`
Expected: All `JournalEntryMapperTests` pass (including the original 8 + the new 3). The two old tests asserting `ContentPreview.Should().BeNull()` / `HighlightedTerms.Should().NotBeNull().And.BeEmpty()` on `ToDto` still pass because we haven't removed the fields from `JournalEntryDto` yet.

- [ ] **Step 5: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Journal/Mapping/JournalEntryMapper.cs \
        backend/test/Anela.Heblo.Tests/Features/Journal/JournalEntryMapperTests.cs
git commit -m "feat(journal): add ToSearchDto mapper that skips Content field"
```

---

## Task 3: Switch `SearchJournalEntriesResponse` and handler to `SearchJournalEntryDto`

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/Journal/Contracts/SearchJournalEntriesResponse.cs`
- Modify: `backend/src/Anela.Heblo.Application/Features/Journal/UseCases/SearchJournalEntries/SearchJournalEntriesHandler.cs`
- Modify: `backend/test/Anela.Heblo.Tests/Features/Journal/SearchJournalEntriesHandlerTests.cs`

- [ ] **Step 1: Write a failing test for the empty-search preview fallback**

Append the following three tests **inside** the `SearchJournalEntriesHandlerTests` class in `backend/test/Anela.Heblo.Tests/Features/Journal/SearchJournalEntriesHandlerTests.cs`, just before the closing `}` of the class (after `CreateJournalEntryWithProductPrefix`):

```csharp
    [Fact]
    public async Task Handle_PopulatesContentPreviewFromDomainContent_WhenSearchTextEmpty()
    {
        // Arrange: 250-char content, no search text. The 200-char window + ellipsis suffix must apply.
        var content = new string('a', 250);
        var entry = new JournalEntry
        {
            Id = 1,
            Title = "Empty search entry",
            Content = content,
            EntryDate = DateTime.Today,
            CreatedAt = DateTime.UtcNow,
            ModifiedAt = DateTime.UtcNow,
            CreatedByUserId = "user-1"
        };

        _repositoryMock
            .Setup(x => x.SearchEntriesAsync(It.IsAny<JournalSearchCriteria>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedResult<JournalEntry>
            {
                Items = new List<JournalEntry> { entry },
                TotalCount = 1,
                PageNumber = 1,
                PageSize = 10
            });

        var request = new SearchJournalEntriesRequest
        {
            SearchText = null,
            PageNumber = 1,
            PageSize = 10
        };

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        var hit = result.Entries.Single();
        hit.ContentPreview.Should().NotBeNull();
        hit.ContentPreview.Should().EndWith("...");
        hit.ContentPreview.Length.Should().BeLessThanOrEqualTo(203); // 200 chars + "..."
        hit.HighlightedTerms.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_BuildsPreviewWindowAroundMatch_WhenSearchTextPresent()
    {
        // Arrange: place "needle" near the middle of a long content string; preview should bracket the match with ellipses on both sides.
        var prefix = new string('p', 300);
        var suffix = new string('s', 300);
        var content = prefix + "needle" + suffix;
        var entry = new JournalEntry
        {
            Id = 7,
            Title = "match",
            Content = content,
            EntryDate = DateTime.Today,
            CreatedAt = DateTime.UtcNow,
            ModifiedAt = DateTime.UtcNow,
            CreatedByUserId = "user-1"
        };

        _repositoryMock
            .Setup(x => x.SearchEntriesAsync(It.IsAny<JournalSearchCriteria>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedResult<JournalEntry>
            {
                Items = new List<JournalEntry> { entry },
                TotalCount = 1,
                PageNumber = 1,
                PageSize = 10
            });

        var request = new SearchJournalEntriesRequest
        {
            SearchText = "needle",
            PageNumber = 1,
            PageSize = 10
        };

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        var hit = result.Entries.Single();
        hit.ContentPreview.Should().Contain("needle");
        hit.ContentPreview.Should().StartWith("...");
        hit.ContentPreview.Should().EndWith("...");
        hit.ContentPreview.Length.Should().BeLessThanOrEqualTo(206); // 200 chars + leading "..." + trailing "..."
    }

    [Fact]
    public async Task Handle_FiltersHighlightTermsToLengthGreaterThanTwo()
    {
        // Arrange: search text mixes short ("a", "is") and long ("needle", "haystack") terms.
        var entry = new JournalEntry
        {
            Id = 9,
            Title = "filter",
            Content = "irrelevant body",
            EntryDate = DateTime.Today,
            CreatedAt = DateTime.UtcNow,
            ModifiedAt = DateTime.UtcNow,
            CreatedByUserId = "user-1"
        };

        _repositoryMock
            .Setup(x => x.SearchEntriesAsync(It.IsAny<JournalSearchCriteria>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedResult<JournalEntry>
            {
                Items = new List<JournalEntry> { entry },
                TotalCount = 1,
                PageNumber = 1,
                PageSize = 10
            });

        var request = new SearchJournalEntriesRequest
        {
            SearchText = "a is needle haystack",
            PageNumber = 1,
            PageSize = 10
        };

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        var hit = result.Entries.Single();
        hit.HighlightedTerms.Should().BeEquivalentTo(new[] { "needle", "haystack" });
    }
```

- [ ] **Step 2: Run the new tests to verify they fail**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~SearchJournalEntriesHandlerTests"`
Expected: The three new tests FAIL (current handler only populates preview when search text is non-empty, and the existing assertions still target `JournalEntryDto` shape). Existing three tests pass.

- [ ] **Step 3: Update the response contract**

Replace the entire content of `backend/src/Anela.Heblo.Application/Features/Journal/Contracts/SearchJournalEntriesResponse.cs` with:

```csharp
using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.Journal.Contracts;

public class SearchJournalEntriesResponse : BaseResponse
{
    public List<SearchJournalEntryDto> Entries { get; set; } = new();
    public int TotalCount { get; set; }
    public int PageNumber { get; set; }
    public int PageSize { get; set; }
    public int TotalPages { get; set; }
    public bool HasNextPage { get; set; }
    public bool HasPreviousPage { get; set; }
}
```

- [ ] **Step 4: Update the handler**

Replace the entire content of `backend/src/Anela.Heblo.Application/Features/Journal/UseCases/SearchJournalEntries/SearchJournalEntriesHandler.cs` with:

```csharp
using Anela.Heblo.Application.Features.Journal.Contracts;
using Anela.Heblo.Application.Features.Journal.Mapping;
using Anela.Heblo.Domain.Features.Journal;
using MediatR;

namespace Anela.Heblo.Application.Features.Journal.UseCases.SearchJournalEntries
{
    public class SearchJournalEntriesHandler : IRequestHandler<SearchJournalEntriesRequest, SearchJournalEntriesResponse>
    {
        private const int PreviewMaxLength = 200;
        private const int MinHighlightTermLength = 3;

        private readonly IJournalRepository _journalRepository;

        public SearchJournalEntriesHandler(IJournalRepository journalRepository)
        {
            _journalRepository = journalRepository;
        }

        public async Task<SearchJournalEntriesResponse> Handle(
            SearchJournalEntriesRequest request,
            CancellationToken cancellationToken)
        {
            var criteria = new JournalSearchCriteria
            {
                SearchText = request.SearchText,
                DateFrom = request.DateFrom,
                DateTo = request.DateTo,
                ProductCodePrefix = request.ProductCodePrefix,
                TagIds = request.TagIds,
                CreatedByUserId = request.CreatedByUserId,
                PageNumber = request.PageNumber,
                PageSize = request.PageSize,
                SortBy = request.SortBy,
                SortDirection = request.SortDirection
            };

            var result = await _journalRepository.SearchEntriesAsync(criteria, cancellationToken);

            var searchText = request.SearchText ?? string.Empty;
            var hasSearchText = !string.IsNullOrEmpty(searchText);

            var entryDtos = result.Items
                .Select(entry =>
                {
                    var dto = JournalEntryMapper.ToSearchDto(entry);
                    dto.ContentPreview = CreateContentPreview(entry.Content, searchText);
                    if (hasSearchText)
                    {
                        dto.HighlightedTerms = ExtractHighlightTerms(searchText);
                    }
                    return dto;
                })
                .ToList();

            return new SearchJournalEntriesResponse
            {
                Entries = entryDtos,
                TotalCount = result.TotalCount,
                PageNumber = request.PageNumber,
                PageSize = request.PageSize,
                TotalPages = (int)Math.Ceiling((double)result.TotalCount / request.PageSize),
                HasNextPage = request.PageNumber * request.PageSize < result.TotalCount,
                HasPreviousPage = request.PageNumber > 1
            };
        }

        private static string CreateContentPreview(string content, string searchText, int maxLength = PreviewMaxLength)
        {
            if (string.IsNullOrEmpty(searchText))
                return content.Length <= maxLength ? content : content[..maxLength] + "...";

            var index = content.IndexOf(searchText, StringComparison.OrdinalIgnoreCase);
            if (index == -1)
                return content.Length <= maxLength ? content : content[..maxLength] + "...";

            var start = Math.Max(0, index - maxLength / 2);
            var length = Math.Min(maxLength, content.Length - start);

            var preview = content.Substring(start, length);
            if (start > 0) preview = "..." + preview;
            if (start + length < content.Length) preview += "...";

            return preview;
        }

        private static List<string> ExtractHighlightTerms(string searchText)
        {
            return searchText.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                            .Where(term => term.Length >= MinHighlightTermLength)
                            .ToList();
        }
    }
}
```

Key behavioural changes vs. the previous handler:
- Projection uses `ToSearchDto` — full `Content` never copied to a DTO field.
- `CreateContentPreview` reads `entry.Content` (domain), not `dto.Content`.
- `ContentPreview` is **always** set (truncated fallback when search text is empty); `HighlightedTerms` is only populated when search text is non-empty.
- `MinHighlightTermLength = 3` (i.e. `term.Length >= 3`) is equivalent to the previous `term.Length > 2`. The constant makes the >2 threshold explicit.

- [ ] **Step 5: Run the full Journal test set**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~Features.Journal"`
Expected: All `SearchJournalEntriesHandlerTests` pass (3 original + 3 new = 6). All `JournalEntryMapperTests` still pass (8 original + 3 new = 11). The other Journal handler tests still pass.

- [ ] **Step 6: Build the whole backend**

Run: `dotnet build backend/Anela.Heblo.sln`
Expected: Build succeeded, 0 errors. (`JournalEntryDto.ContentPreview` and `.HighlightedTerms` are still declared but unused; they will be removed in Task 4.)

- [ ] **Step 7: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Journal/Contracts/SearchJournalEntriesResponse.cs \
        backend/src/Anela.Heblo.Application/Features/Journal/UseCases/SearchJournalEntries/SearchJournalEntriesHandler.cs \
        backend/test/Anela.Heblo.Tests/Features/Journal/SearchJournalEntriesHandlerTests.cs
git commit -m "feat(journal): switch search response to SearchJournalEntryDto and always populate preview"
```

---

## Task 4: Remove search-only fields from `JournalEntryDto`

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/Journal/Contracts/JournalEntryDto.cs`
- Modify: `backend/test/Anela.Heblo.Tests/Features/Journal/JournalEntryMapperTests.cs`

- [ ] **Step 1: Remove the two obsolete tests on `JournalEntryDto`**

Delete the following two tests from `backend/test/Anela.Heblo.Tests/Features/Journal/JournalEntryMapperTests.cs` (current lines 181–205, the `ToDto_ContentPreview_IsNullAfterMapping` and `ToDto_HighlightedTerms_IsEmptyAfterMapping` facts). The replacement coverage already lives on `ToSearchDto` (added in Task 2 — `ToSearchDto_LeavesContentPreviewEmpty_AndHighlightedTermsEmpty`).

After deletion, the file's tail (last test in the class) is `ToDto_Tags_IsEmptyList_WhenNoTagAssignments` followed by the three `ToSearchDto_*` tests added in Task 2.

- [ ] **Step 2: Run the mapper tests to confirm green**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~JournalEntryMapperTests"`
Expected: All mapper tests pass (was 11, now 9 after removing the two obsolete tests).

- [ ] **Step 3: Remove the two fields from `JournalEntryDto`**

Replace the entire content of `backend/src/Anela.Heblo.Application/Features/Journal/Contracts/JournalEntryDto.cs` with:

```csharp
using System;
using System.Collections.Generic;

namespace Anela.Heblo.Application.Features.Journal.Contracts
{
    public class JournalEntryDto
    {
        public int Id { get; set; }
        public string? Title { get; set; }
        public string Content { get; set; } = null!;
        public DateTime EntryDate { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime ModifiedAt { get; set; }
        public string CreatedByUserId { get; set; } = null!;
        public string? CreatedByUsername { get; set; }
        public string? ModifiedByUserId { get; set; }
        public string? ModifiedByUsername { get; set; }

        public List<string> AssociatedProducts { get; set; } = new();
        public List<JournalEntryTagDto> Tags { get; set; } = new();
    }

    public class JournalEntryTagDto
    {
        public int Id { get; set; }
        public string Name { get; set; } = null!;
        public string Color { get; set; } = null!;
    }
}
```

- [ ] **Step 4: Build the backend and run all Journal tests**

Run: `dotnet build backend/Anela.Heblo.sln`
Expected: Build succeeded, 0 errors.

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~Features.Journal"`
Expected: All Journal tests pass.

- [ ] **Step 5: Run `dotnet format`**

Run: `dotnet format backend/Anela.Heblo.sln`
Expected: No issues, or any whitespace adjustments staged automatically.

- [ ] **Step 6: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Journal/Contracts/JournalEntryDto.cs \
        backend/test/Anela.Heblo.Tests/Features/Journal/JournalEntryMapperTests.cs
git commit -m "refactor(journal): drop search-only fields from JournalEntryDto"
```

---

## Task 5: Regenerate the TypeScript API client

**Files:**
- Modify (auto-regenerated): `frontend/src/api/generated/api-client.ts`

NSwag regenerates `api-client.ts` via a PostBuild target on the API project in **Debug** configuration. Reference: `docs/development/api-client-generation.md`.

- [ ] **Step 1: Rebuild the API project in Debug to trigger NSwag**

Run: `dotnet build backend/src/Anela.Heblo.API/Anela.Heblo.API.csproj -c Debug`
Expected: Build succeeded. NSwag log lines appear; `frontend/src/api/generated/api-client.ts` is rewritten.

- [ ] **Step 2: Verify the regenerated client matches the new contract**

Run (from the worktree root):
```bash
grep -n "class JournalEntryDto\|class SearchJournalEntryDto\|contentPreview\|highlightedTerms" frontend/src/api/generated/api-client.ts
```

Expected output (line numbers may differ):
- One `export class JournalEntryDto` definition with **no** `contentPreview` / `highlightedTerms` properties.
- One `export class SearchJournalEntryDto` definition that has both `contentPreview` (string) and `highlightedTerms` (string[]).
- `IJournalEntryDto` interface mirrors `JournalEntryDto` (no removed fields).
- `IGetJournalEntriesResponse.entries` and the new `ISearchJournalEntriesResponse.entries` (or equivalent) reflect the correct element types.

If `JournalEntryDto` still contains `contentPreview` or `highlightedTerms`, the rebuild did not run NSwag. Re-run the build, or run `dotnet msbuild backend/src/Anela.Heblo.API -t:GenerateBackendClient` per the docs.

- [ ] **Step 3: Stage the regenerated client**

```bash
git add frontend/src/api/generated/api-client.ts
```

Do **not** commit yet — the frontend will not type-check until Task 6. Combine the regen with the frontend fix into one commit.

---

## Task 6: Update `JournalList.tsx` to use the typed split

**Files:**
- Modify: `frontend/src/components/pages/Journal/JournalList.tsx`

Architecture-review prescription: split the `.map` into two branches keyed on `isSearchMode`. No `as` casts. No runtime nullable check as a discriminator.

- [ ] **Step 1: Add the new type import**

In `frontend/src/components/pages/Journal/JournalList.tsx`, update the import on line 20 from:

```typescript
import type { JournalEntryDto } from "../../../api/generated/api-client";
```

to:

```typescript
import type {
  JournalEntryDto,
  SearchJournalEntryDto,
} from "../../../api/generated/api-client";
```

- [ ] **Step 2: Extract a shared row-rendering helper**

Add the following helper component **above** the `JournalList` component definition (after the imports, before `const JournalList: React.FC = () => {`):

```typescript
interface JournalRowProps {
  id: number;
  title?: string;
  entryDate: Date;
  authorLabel: string;
  contentText: string;
  tags?: { id?: number; name?: string; color?: string }[];
  associatedProducts?: string[];
  onClick: () => void;
}

const JournalRow: React.FC<JournalRowProps> = ({
  id,
  title,
  entryDate,
  authorLabel,
  contentText,
  tags,
  associatedProducts,
  onClick,
}) => (
  <tr
    key={id}
    className="hover:bg-gray-50 cursor-pointer transition-colors duration-150"
    data-testid="journal-entry"
    onClick={onClick}
    title="Klikněte pro editaci záznamu"
  >
    <td className="px-4 py-4 whitespace-nowrap text-sm font-medium text-gray-900">
      <div className="max-w-48 truncate">{title || "Bez názvu"}</div>
    </td>
    <td className="px-4 py-4 whitespace-nowrap text-sm text-gray-900">
      {format(new Date(entryDate), "dd.MM.yyyy")}
    </td>
    <td className="px-4 py-4 whitespace-nowrap text-sm text-gray-500">
      <div className="max-w-32 truncate">{authorLabel}</div>
    </td>
    <td className="px-4 py-4 text-sm text-gray-700">
      <div className="max-w-96 line-clamp-2">{contentText}</div>
    </td>
    <td className="px-4 py-4 text-sm">
      <div className="flex flex-wrap gap-1 max-w-48">
        {tags &&
          tags.slice(0, 2).map((tag) => (
            <span
              key={tag.id}
              className="inline-flex items-center px-2 py-0.5 rounded-full text-xs font-medium border"
              style={{ borderColor: tag.color, color: tag.color }}
            >
              {tag.name}
            </span>
          ))}
        {tags && tags.length > 2 && (
          <span className="inline-flex items-center px-2 py-0.5 rounded-full text-xs font-medium bg-gray-100 text-gray-600">
            +{tags.length - 2}
          </span>
        )}
      </div>
    </td>
    <td className="px-4 py-4 text-sm">
      <div className="flex flex-wrap gap-1 max-w-32">
        {associatedProducts?.slice(0, 2).map((product) => (
          <span
            key={product}
            className="inline-flex items-center px-2 py-0.5 rounded text-xs font-medium bg-indigo-100 text-indigo-800"
          >
            {product}
          </span>
        ))}
        {associatedProducts && associatedProducts.length > 2 && (
          <span className="inline-flex items-center px-2 py-0.5 rounded text-xs font-medium bg-gray-100 text-gray-600">
            +{associatedProducts.length - 2}
          </span>
        )}
      </div>
    </td>
  </tr>
);
```

- [ ] **Step 3: Replace the existing `.map` body with the typed split**

In `JournalList.tsx`, locate the `<tbody>` element (currently around lines 312–385). Replace the entire `<tbody>...</tbody>` block with:

```tsx
              <tbody className="bg-white divide-y divide-gray-200">
                {isSearchMode
                  ? (entries as SearchJournalEntryDto[]).map((entry) => (
                      <JournalRow
                        key={entry.id!}
                        id={entry.id!}
                        title={entry.title ?? undefined}
                        entryDate={entry.entryDate!}
                        authorLabel={entry.createdByUsername || entry.createdByUserId!}
                        contentText={entry.contentPreview!}
                        tags={entry.tags}
                        associatedProducts={entry.associatedProducts}
                        onClick={() => handleOpenEditModal(entry.id!)}
                      />
                    ))
                  : (entries as JournalEntryDto[]).map((entry) => (
                      <JournalRow
                        key={entry.id!}
                        id={entry.id!}
                        title={entry.title ?? undefined}
                        entryDate={entry.entryDate!}
                        authorLabel={entry.createdByUsername || entry.createdByUserId!}
                        contentText={truncateContent(entry.content!, 150)}
                        tags={entry.tags}
                        associatedProducts={entry.associatedProducts}
                        onClick={() => handleOpenEditModal(entry.id!)}
                      />
                    ))}
              </tbody>
```

Why this shape works given the existing `entries` declaration:
- The hooks return different result types (`GetJournalEntriesResponse` vs. `SearchJournalEntriesResponse`), but the local `entries` variable widens to a union — TypeScript will not auto-narrow inside `.map`. The `isSearchMode ? (… as SearchJournalEntryDto[]) : (… as JournalEntryDto[])` pattern is a **runtime-correct, mode-driven assertion**, not a defensive cast away from a known type. The arch-review explicitly disallows casts inside the map body to mask drift; here the cast is on the outer array at the branch boundary, which is the architecturally sanctioned split point.
- If you want zero casts: change `useJournal.ts` to use two separately-typed state variables (`searchEntries: SearchJournalEntryDto[]`, `listEntries: JournalEntryDto[]`) and read from the appropriate one per branch. This is out of scope for this plan — the current branch-boundary assertion is acceptable.

- [ ] **Step 4: Type-check the frontend**

Run: `cd frontend && npm run build`
Expected: Build succeeded with no TypeScript errors. (CRA's `react-scripts build` runs `tsc` as part of the build.)

If the build complains about unused `JournalEntryDto` / `SearchJournalEntryDto` imports, double-check that both are actually referenced. The import line must reference both type names; if only one is used in the file, drop the unused one — do **not** keep dead imports.

- [ ] **Step 5: Lint the frontend**

Run: `cd frontend && npm run lint`
Expected: No errors.

- [ ] **Step 6: Run the frontend tests**

Run: `cd frontend && npm test -- --watchAll=false`
Expected: All tests pass. (If a Journal-specific Jest test exists, it should still pass; the rendered DOM structure for rows is unchanged.)

- [ ] **Step 7: Commit (combine FE changes with regenerated client)**

```bash
git add frontend/src/api/generated/api-client.ts \
        frontend/src/components/pages/Journal/JournalList.tsx
git commit -m "refactor(journal): consume SearchJournalEntryDto via typed row split"
```

---

## Task 7: Cross-touch verification and final build

**Files:**
- (verification only)

- [ ] **Step 1: Confirm no remaining references to the removed fields**

Run (from the worktree root):
```bash
grep -rn "contentPreview\|highlightedTerms\|ContentPreview\|HighlightedTerms" \
  backend/src backend/test frontend/src \
  --include="*.cs" --include="*.ts" --include="*.tsx" \
  2>/dev/null | grep -v "api-client.ts" | grep -v "SearchJournalEntryDto"
```

Expected: No matches outside `SearchJournalEntryDto.cs`, `SearchJournalEntriesHandler.cs`, the new `SearchJournalEntryDto`-targeted tests, and the regenerated `api-client.ts`. The match in `api-client.ts` is allowed (it now lives only on `SearchJournalEntryDto`).

If anything else matches, investigate before continuing. The catalog widget (`JournalTab.tsx`, chart helpers, etc.) only consumes `JournalEntryDto` for non-search reads; they must compile cleanly.

- [ ] **Step 2: Final backend build + format + tests**

Run sequentially:
```bash
dotnet build backend/Anela.Heblo.sln
dotnet format backend/Anela.Heblo.sln --verify-no-changes
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj
```
Expected: Build OK, format OK (no changes needed), tests all pass.

If `dotnet format --verify-no-changes` reports diffs, re-run `dotnet format backend/Anela.Heblo.sln` and amend the formatting into a follow-up commit (`chore: dotnet format`).

- [ ] **Step 3: Final frontend build + lint**

Run sequentially:
```bash
cd frontend
npm run build
npm run lint
cd ..
```
Expected: Both succeed cleanly.

- [ ] **Step 4: Smoke-test the search and list paths**

Skip if you cannot start the local stack. Otherwise:

```bash
# Terminal 1 (BE)
dotnet run --project backend/src/Anela.Heblo.API

# Terminal 2 (FE)
cd frontend && npm start
```

Manually verify in the browser at `http://localhost:3000`:
- Navigate to Deník (Journal). Rows render with full content truncated to 150 chars.
- Type a search term and click "Filtrovat". Rows now show the 200-char preview window with the term roughly in the middle, bracketed by ellipses when the content overflows.
- Click "Vymazat" — rows return to the full-content truncated rendering.
- Inspect the Network tab on the search request: `entries[*].content` must be **absent** from the JSON payload; `entries[*].contentPreview` must be a non-null string.

If you cannot run the stack locally, say so explicitly in the final report — do **not** claim the smoke test passed without evidence.

- [ ] **Step 5: No commit needed**

This task is verification only. If `dotnet format` ran cleanly and no diff was produced, nothing to commit.

If a `dotnet format` follow-up commit was needed in Step 2:
```bash
git add -u backend
git commit -m "chore: dotnet format after journal DTO split"
```

---

## Out-of-Scope (Explicit)

The spec and architecture review both call out the following as intentionally out of scope. **Do not implement these in this plan**:
- Removing or renaming `Content` from `JournalEntryDto` (list/detail still need full content).
- Adding `<mark>`-style server-side highlight markup; current "raw preview + terms list" is preserved.
- Changing pagination, sort fields, or search criteria.
- Versioned/deprecated shims for the removed fields.
- An OpenAPI contract test against `swagger.json` (mentioned as an optional FR-6 amendment; skip unless the codebase already has an analogous test that breaks — there is none today).
- Fixing the latent mid-multibyte preview window bug; tracked separately.

## Risk Recap

| Risk | Mitigation in plan |
|------|--------------------|
| NSwag does not regenerate the TS client | Task 5 Step 2 explicitly verifies the client contains the new type via grep before progressing. |
| Existing FE code outside `JournalList` silently relied on the removed fields | Task 7 Step 1 grep across `frontend/src` (excluding `api-client.ts`) confirms no leftover references. |
| `dotnet format` rewrites unrelated files | Task 4 Step 5 runs format right after the backend change set is stable; Task 7 Step 2 verifies idempotency. |
| `JournalRow` extraction subtly changes rendered DOM | The element tree is identical to the original `tr/td` JSX; only the data source per cell is parametrized. Visual smoke test in Task 7 Step 4 catches any regression. |

## Self-Review Notes (after writing the plan)

- **Spec coverage**: FR-1 → Task 1; FR-2 → Task 4; FR-3 → Task 3 (response type + handler); FR-4 → Task 2 (mapper) + Task 3 (handler reads `entry.Content`, not DTO field); FR-5 → Tasks 5 + 6 (regen client + JournalList split); FR-6 → Tasks 2 + 3 + 4 (backend tests) and Task 6 Step 6 (FE tests run). NFR-1 covered by Task 3's mapper change (no `Content` allocation onto search DTO). NFR-2 covered by the "single Docker image" deploy model — backend and FE ship together via the regen step. NFR-3/NFR-4 are inherent to the new shape.
- **Architecture-review amendments**: (1) `ContentPreview = string.Empty` default in the mapper — Task 1 + Task 2 Step 3. (2) `HighlightedTerms` stays empty when search text is empty — Task 3 Step 4 handler logic. (3) Empty-search preview fallback always populated — Task 3 Step 4 + Task 3 Step 1 test. (4) Frontend uses typed split, not nullable discriminator — Task 6 Step 3. The optional OpenAPI contract test (amendment #3 in arch-review) is documented as out of scope above.
- **Placeholders**: None — every code step contains the complete code or full command.
- **Type consistency**: `ToSearchDto` name is consistent across Tasks 2 and 3. `SearchJournalEntryDto` name is consistent across Tasks 1, 3, 5, 6. `JournalRow` helper interface aligns with the props passed in both branches of Task 6 Step 3.
