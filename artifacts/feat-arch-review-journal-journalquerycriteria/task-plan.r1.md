# Remove Journal Query/Search Criteria from Domain Layer — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Eliminate `JournalQueryCriteria` and `JournalSearchCriteria` from the Domain layer; `IJournalRepository` accepts primitive query parameters directly; handlers pass `request.*` fields straight through.

**Architecture:** Pure backend refactor. Public HTTP/OpenAPI surface and JSON shapes are unchanged. Repository interface gains long primitive parameter lists (matching the existing `ILotRepository` / `IEanRepository` convention). The mechanical translation step in handlers is deleted. No schema, DI, or config changes.

**Tech Stack:** .NET 8 · C# · MediatR · EF Core · xUnit · Moq · FluentAssertions.

---

## Pre-flight Checks

Run these once before starting and once after the final commit. They are the acceptance gates from `spec.r1.md` §FR-1 / NFR-2 and the arch review §"Specification Amendments".

```bash
# All run from repo root.

# Confirm baseline (BEFORE making changes): there are 7 files referencing the criteria types.
grep -r "JournalQueryCriteria\|JournalSearchCriteria" backend/src backend/test | wc -l
# Expected before: > 0 (today it's ~14 matches across 7 files)

# Confirm Domain has no Application reference today (must remain empty after refactor).
grep -R "Anela.Heblo.Application" backend/src/Anela.Heblo.Domain
# Expected: empty output
```

After the final commit, both `grep -r "JournalQueryCriteria\|JournalSearchCriteria" backend/src backend/test` and `grep -R "Anela.Heblo.Application" backend/src/Anela.Heblo.Domain` MUST produce no output.

---

## File Structure

Files affected (no new files, no `.csproj` edits):

**Delete (Domain):**
- `backend/src/Anela.Heblo.Domain/Features/Journal/JournalQueryCriteria.cs`
- `backend/src/Anela.Heblo.Domain/Features/Journal/JournalSearchCriteria.cs`

**Modify (Domain):**
- `backend/src/Anela.Heblo.Domain/Features/Journal/IJournalRepository.cs` — replace two method signatures with primitive parameter lists.

**Modify (Persistence):**
- `backend/src/Anela.Heblo.Persistence/Catalog/Journal/JournalRepository.cs` — implement the new signatures; preserve LINQ behavior 1:1.

**Modify (Application):**
- `backend/src/Anela.Heblo.Application/Features/Journal/UseCases/GetJournalEntries/GetJournalEntriesHandler.cs` — remove `JournalQueryCriteria` allocation, call `_journalRepository.GetEntriesAsync(...)` directly.
- `backend/src/Anela.Heblo.Application/Features/Journal/UseCases/SearchJournalEntries/SearchJournalEntriesHandler.cs` — remove `JournalSearchCriteria` allocation, call `_journalRepository.SearchEntriesAsync(...)` directly using **named arguments** (per arch-review §"Specification Amendments" item 1).

**Modify (Tests):**
- `backend/test/Anela.Heblo.Tests/Features/Journal/SearchJournalEntriesHandlerTests.cs` — change every `It.IsAny<JournalSearchCriteria>()` / `It.Is<JournalSearchCriteria>(...)` matcher to per-parameter matchers (`It.IsAny<...>()` / `It.Is<...>(...)`) on the matching parameter position.

---

## Task 0: Verify clean working tree and baseline grep

**Files:** none (read-only).

- [ ] **Step 1: Confirm clean working tree**

Run: `git status`
Expected: `nothing to commit, working tree clean`.

- [ ] **Step 2: Baseline-grep the criteria types**

Run: `grep -rn "JournalQueryCriteria\|JournalSearchCriteria" backend/src backend/test`
Expected: matches in exactly these 7 files (use this to compare against the final post-refactor grep):
```
backend/src/Anela.Heblo.Application/Features/Journal/UseCases/GetJournalEntries/GetJournalEntriesHandler.cs
backend/src/Anela.Heblo.Application/Features/Journal/UseCases/SearchJournalEntries/SearchJournalEntriesHandler.cs
backend/src/Anela.Heblo.Domain/Features/Journal/IJournalRepository.cs
backend/src/Anela.Heblo.Domain/Features/Journal/JournalQueryCriteria.cs
backend/src/Anela.Heblo.Domain/Features/Journal/JournalSearchCriteria.cs
backend/src/Anela.Heblo.Persistence/Catalog/Journal/JournalRepository.cs
backend/test/Anela.Heblo.Tests/Features/Journal/SearchJournalEntriesHandlerTests.cs
```

- [ ] **Step 3: Baseline-confirm Domain ↛ Application**

Run: `grep -R "Anela.Heblo.Application" backend/src/Anela.Heblo.Domain`
Expected: empty.

- [ ] **Step 4: Run Journal tests once to confirm green baseline**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~Journal" --nologo`
Expected: all tests pass. Capture the number; the same number must pass after the refactor (no test deletions in this plan).

---

## Task 1: Rewrite `IJournalRepository` with primitive parameter signatures

**Files:**
- Modify: `backend/src/Anela.Heblo.Domain/Features/Journal/IJournalRepository.cs`

**Rationale:** Spec FR-2. The new interface uses primitive parameters that match the existing convention in `ILotRepository` / `IEanRepository`. `IReadOnlyCollection<int>?` for `tagIds` per arch-review §"Specification Amendments" item 2 (implicit conversion from `List<int>?` at the call site is fine).

- [ ] **Step 1: Replace the file contents**

Overwrite `backend/src/Anela.Heblo.Domain/Features/Journal/IJournalRepository.cs` with exactly:

```csharp
using Anela.Heblo.Xcc.Persistance;

namespace Anela.Heblo.Domain.Features.Journal
{
    public interface IJournalRepository : IRepository<JournalEntry, int>
    {
        Task<PagedResult<JournalEntry>> GetEntriesAsync(
            int pageNumber,
            int pageSize,
            string sortBy,
            string sortDirection,
            CancellationToken cancellationToken = default);

        Task<PagedResult<JournalEntry>> SearchEntriesAsync(
            string? searchText,
            DateTime? dateFrom,
            DateTime? dateTo,
            string? productCodePrefix,
            IReadOnlyCollection<int>? tagIds,
            string? createdByUserId,
            int pageNumber,
            int pageSize,
            string sortBy,
            string sortDirection,
            CancellationToken cancellationToken = default);

        Task<List<JournalEntry>> GetEntriesByProductAsync(
            string productCode,
            CancellationToken cancellationToken = default);

        Task<Dictionary<string, JournalIndicator>> GetJournalIndicatorsAsync(
            IEnumerable<string> productCodes,
            CancellationToken cancellationToken = default);
    }
}
```

- [ ] **Step 2: Compile-fail check (intentional)**

Run: `dotnet build backend/src/Anela.Heblo.Domain/Anela.Heblo.Domain.csproj --nologo`
Expected: the Domain project itself **builds clean** (the deleted types are not yet deleted, so the file still exists alongside; that's fine — they will be deleted in Task 2). The new interface compiles because it only uses BCL + Xcc types.

Run: `dotnet build backend/Anela.Heblo.sln --nologo`
Expected: **FAIL** with errors in `JournalRepository.cs` (Persistence) and both handlers — they still reference the old `JournalQueryCriteria` / `JournalSearchCriteria` overloads. This is the expected RED state — those references are fixed in Tasks 3-5. Do not attempt to satisfy the compiler yet.

**Do not commit at this checkpoint.** The build is red on purpose; we'll restore green inside this same logical change. (Frequent commits are good, but committing a known-broken solution would block bisect later.) Commit after Task 5.

---

## Task 2: Delete the two criteria files from Domain

**Files:**
- Delete: `backend/src/Anela.Heblo.Domain/Features/Journal/JournalQueryCriteria.cs`
- Delete: `backend/src/Anela.Heblo.Domain/Features/Journal/JournalSearchCriteria.cs`

**Rationale:** Spec FR-1. After this step, any remaining reference becomes a compile-time error rather than a silently-accepted dead type.

- [ ] **Step 1: Delete the files**

```bash
git rm backend/src/Anela.Heblo.Domain/Features/Journal/JournalQueryCriteria.cs
git rm backend/src/Anela.Heblo.Domain/Features/Journal/JournalSearchCriteria.cs
```

- [ ] **Step 2: Confirm deletion**

Run: `ls backend/src/Anela.Heblo.Domain/Features/Journal/ | grep -i criteria`
Expected: empty output.

Run: `grep -rn "JournalQueryCriteria\|JournalSearchCriteria" backend/src/Anela.Heblo.Domain`
Expected: empty output. (Only handlers and tests still reference the type names; those are fixed in Tasks 3-6.)

---

## Task 3: Update `JournalRepository` (Persistence) to implement the new signatures

**Files:**
- Modify: `backend/src/Anela.Heblo.Persistence/Catalog/Journal/JournalRepository.cs`

**Rationale:** Spec FR-3. The two `*Async` method bodies translate field-for-field from the old `criteria.X` accesses to the corresponding local parameter. Sort logic, filter logic, includes, `IsDeleted` filter, `PagedResult` projection are all preserved verbatim. Per the arch-review §"Critical preserved behaviors":

- `searchText`: trim + `ToLower()`, `Contains` against `Title` and `Content`.
- `dateFrom` / `dateTo`: `>= Date` / `<= Date` on `EntryDate`.
- `productCodePrefix`: **`productCodePrefix.StartsWith(pa.ProductCodePrefix)`** — request value starts with stored prefix; counter-intuitive direction; **do not "fix"**.
- `tagIds`: applied when non-null and non-empty.
- `createdByUserId`: equality when non-empty.
- Sort: `sortBy?.ToLower()` switch over `"title"`, `"createdat"`, default → `EntryDate`. `sortDirection == "ASC"` → ascending; anything else → descending.

- [ ] **Step 1: Replace the two query method bodies**

In `backend/src/Anela.Heblo.Persistence/Catalog/Journal/JournalRepository.cs`, replace `GetEntriesAsync` (lines 28-67) and `SearchEntriesAsync` (lines 69-149) with the implementations below. Leave the rest of the file (constructor, `GetByIdAsync`, `GetEntriesByProductAsync`, `GetJournalIndicatorsAsync`, using directives, namespace) untouched.

```csharp
        public async Task<PagedResult<JournalEntry>> GetEntriesAsync(
            int pageNumber,
            int pageSize,
            string sortBy,
            string sortDirection,
            CancellationToken cancellationToken = default)
        {
            var query = Context.Set<JournalEntry>()
                .Include(x => x.ProductAssociations)
                .Include(x => x.TagAssignments)
                    .ThenInclude(x => x.Tag)
                .Where(x => !x.IsDeleted)
                .AsQueryable();

            // Sorting
            query = sortBy?.ToLower() switch
            {
                "title" => sortDirection == "ASC"
                    ? query.OrderBy(x => x.Title)
                    : query.OrderByDescending(x => x.Title),
                "createdat" => sortDirection == "ASC"
                    ? query.OrderBy(x => x.CreatedAt)
                    : query.OrderByDescending(x => x.CreatedAt),
                _ => sortDirection == "ASC"
                    ? query.OrderBy(x => x.EntryDate)
                    : query.OrderByDescending(x => x.EntryDate)
            };

            var totalCount = await query.CountAsync(cancellationToken);

            var items = await query
                .Skip((pageNumber - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync(cancellationToken);

            return new PagedResult<JournalEntry>
            {
                Items = items,
                TotalCount = totalCount,
                PageNumber = pageNumber,
                PageSize = pageSize
            };
        }

        public async Task<PagedResult<JournalEntry>> SearchEntriesAsync(
            string? searchText,
            DateTime? dateFrom,
            DateTime? dateTo,
            string? productCodePrefix,
            IReadOnlyCollection<int>? tagIds,
            string? createdByUserId,
            int pageNumber,
            int pageSize,
            string sortBy,
            string sortDirection,
            CancellationToken cancellationToken = default)
        {
            var query = Context.Set<JournalEntry>()
                .Include(x => x.ProductAssociations)
                .Include(x => x.TagAssignments)
                    .ThenInclude(x => x.Tag)
                .Where(x => !x.IsDeleted)
                .AsQueryable();

            // Text search (simple contains for now, can be improved with full-text search)
            if (!string.IsNullOrEmpty(searchText))
            {
                var searchTerm = searchText.Trim().ToLower();
                query = query.Where(x =>
                    (x.Title != null && x.Title.ToLower().Contains(searchTerm)) ||
                    x.Content.ToLower().Contains(searchTerm));
            }

            // Date filtering
            if (dateFrom.HasValue)
            {
                query = query.Where(x => x.EntryDate >= dateFrom.Value.Date);
            }

            if (dateTo.HasValue)
            {
                query = query.Where(x => x.EntryDate <= dateTo.Value.Date);
            }

            // Product filtering - check if requested product code prefix starts with any stored prefix
            if (!string.IsNullOrEmpty(productCodePrefix))
            {
                query = query.Where(x => x.ProductAssociations
                    .Any(pa => productCodePrefix.StartsWith(pa.ProductCodePrefix)));
            }


            // Tag filtering
            if (tagIds?.Any() == true)
            {
                query = query.Where(x => x.TagAssignments
                    .Any(ta => tagIds.Contains(ta.TagId)));
            }

            // User filtering
            if (!string.IsNullOrEmpty(createdByUserId))
            {
                query = query.Where(x => x.CreatedByUserId == createdByUserId);
            }

            // Sorting
            query = sortBy?.ToLower() switch
            {
                "title" => sortDirection == "ASC"
                    ? query.OrderBy(x => x.Title)
                    : query.OrderByDescending(x => x.Title),
                "createdat" => sortDirection == "ASC"
                    ? query.OrderBy(x => x.CreatedAt)
                    : query.OrderByDescending(x => x.CreatedAt),
                _ => sortDirection == "ASC"
                    ? query.OrderBy(x => x.EntryDate)
                    : query.OrderByDescending(x => x.EntryDate)
            };

            var totalCount = await query.CountAsync(cancellationToken);

            var items = await query
                .Skip((pageNumber - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync(cancellationToken);

            return new PagedResult<JournalEntry>
            {
                Items = items,
                TotalCount = totalCount,
                PageNumber = pageNumber,
                PageSize = pageSize
            };
        }
```

- [ ] **Step 2: Verify the Persistence project compiles**

Run: `dotnet build backend/src/Anela.Heblo.Persistence/Anela.Heblo.Persistence.csproj --nologo`
Expected: **PASS**. (The two handlers in Application still don't compile against the new interface — that's the next two tasks. The solution build remains red until Task 5.)

---

## Task 4: Simplify `GetJournalEntriesHandler`

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/Journal/UseCases/GetJournalEntries/GetJournalEntriesHandler.cs`

**Rationale:** Spec FR-4. Delete the `JournalQueryCriteria` allocation; pass `request.*` fields positionally (only four parameters — positional is fine here per arch-review which only mandates named args for the 11-param `SearchEntriesAsync`).

- [ ] **Step 1: Replace the file contents**

Overwrite `backend/src/Anela.Heblo.Application/Features/Journal/UseCases/GetJournalEntries/GetJournalEntriesHandler.cs` with:

```csharp
using Anela.Heblo.Application.Features.Journal.Contracts;
using Anela.Heblo.Application.Features.Journal.Mapping;
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

            return new GetJournalEntriesResponse
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
    }
}
```

- [ ] **Step 2: Confirm no `JournalQueryCriteria` references remain in this file**

Run: `grep -n "JournalQueryCriteria" backend/src/Anela.Heblo.Application/Features/Journal/UseCases/GetJournalEntries/GetJournalEntriesHandler.cs`
Expected: empty output.

---

## Task 5: Simplify `SearchJournalEntriesHandler` (named arguments at the 11-parameter call site)

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/Journal/UseCases/SearchJournalEntries/SearchJournalEntriesHandler.cs`

**Rationale:** Spec FR-5, **amended by arch-review §"Specification Amendments" item 1**: positional invocation forbidden because 11 same-typed-looking parameters are silently broken by argument reordering during future maintenance. `CreateContentPreview` / `ExtractHighlightTerms` and response shaping are unchanged.

- [ ] **Step 1: Replace the file contents**

Overwrite `backend/src/Anela.Heblo.Application/Features/Journal/UseCases/SearchJournalEntries/SearchJournalEntriesHandler.cs` with:

```csharp
using Anela.Heblo.Application.Features.Journal.Contracts;
using Anela.Heblo.Application.Features.Journal.Mapping;
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

        private static string CreateContentPreview(string content, string searchText, int maxLength = 200)
        {
            if (string.IsNullOrEmpty(content)) return string.Empty;

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
                            .Where(term => term.Length > 2)
                            .ToList();
        }
    }
}
```

- [ ] **Step 2: Build the full solution**

Run: `dotnet build backend/Anela.Heblo.sln --nologo`
Expected: **PASS** with zero new warnings. The test project still references `JournalSearchCriteria` (it's the test class fixed in Task 6) — those references generate compile errors **inside the test project only**, so the test project's build fails. That's expected. Verify with:

Run: `dotnet build backend/src/Anela.Heblo.Application/Anela.Heblo.Application.csproj --nologo`
Expected: **PASS** with zero new warnings.

Run: `dotnet build backend/src/Anela.Heblo.Persistence/Anela.Heblo.Persistence.csproj --nologo`
Expected: **PASS**.

Run: `dotnet build backend/src/Anela.Heblo.Domain/Anela.Heblo.Domain.csproj --nologo`
Expected: **PASS**.

(Once Task 6 is done, the entire solution including tests builds clean.)

- [ ] **Step 3: First commit — production source is fully refactored**

Commit only the production-source changes plus the file deletions. The test fix is its own commit (clearer history; bisect-friendly).

```bash
git add \
  backend/src/Anela.Heblo.Domain/Features/Journal/IJournalRepository.cs \
  backend/src/Anela.Heblo.Domain/Features/Journal/JournalQueryCriteria.cs \
  backend/src/Anela.Heblo.Domain/Features/Journal/JournalSearchCriteria.cs \
  backend/src/Anela.Heblo.Persistence/Catalog/Journal/JournalRepository.cs \
  backend/src/Anela.Heblo.Application/Features/Journal/UseCases/GetJournalEntries/GetJournalEntriesHandler.cs \
  backend/src/Anela.Heblo.Application/Features/Journal/UseCases/SearchJournalEntries/SearchJournalEntriesHandler.cs

git commit -m "refactor(journal): remove domain query/search criteria types

IJournalRepository now accepts primitive query parameters directly.
Handlers pass request fields through (named args at the 11-param search
call site to defend against silent argument reordering during future
maintenance). LINQ behavior preserved 1:1.

Brings IJournalRepository in line with the convention already used by
ILotRepository / IEanRepository and restores Clean Architecture layering
(System.ComponentModel.DataAnnotations is no longer pulled into Domain
on behalf of these query plumbing types)."
```

Note: the commit message attribution is disabled globally per the user's git-workflow rules, so no Co-Authored-By footer.

---

## Task 6: Update `SearchJournalEntriesHandlerTests` to verify per-parameter matchers

**Files:**
- Modify: `backend/test/Anela.Heblo.Tests/Features/Journal/SearchJournalEntriesHandlerTests.cs`

**Rationale:** Spec FR-6. The current tests mock `SearchEntriesAsync` with a single `It.IsAny<JournalSearchCriteria>()` matcher and verify a specific criteria field via `It.Is<JournalSearchCriteria>(r => r.ProductCodePrefix == "TON002")`. After the refactor, the mock must match against the 11 parameters individually, and the `Verify(...)` call must check the specific parameter (`productCodePrefix`) at its position.

The new `SearchEntriesAsync` signature is:
```
SearchEntriesAsync(
    string? searchText,
    DateTime? dateFrom,
    DateTime? dateTo,
    string? productCodePrefix,
    IReadOnlyCollection<int>? tagIds,
    string? createdByUserId,
    int pageNumber,
    int pageSize,
    string sortBy,
    string sortDirection,
    CancellationToken cancellationToken)
```

Strategy for each test:
- `Setup(...)` uses `It.IsAny<>()` for every parameter — these tests assert handler behavior given a known repository response, not that the handler passes a specific filter.
- `Verify(...)` in the single existing test that asserts the prefix flows through (`SearchByProductCodePrefix_ShouldReturnEntriesWithMatchingPrefix`, lines 68-71) must use `It.Is<string?>(p => p == "TON002")` at the `productCodePrefix` position and `It.IsAny<>()` elsewhere. **The `productCodePrefix` parameter MUST be at position 4** — count carefully.

- [ ] **Step 1: Replace the file contents**

Overwrite `backend/test/Anela.Heblo.Tests/Features/Journal/SearchJournalEntriesHandlerTests.cs` with:

```csharp
using Anela.Heblo.Application.Features.Journal.Contracts;
using Anela.Heblo.Application.Features.Journal.UseCases.SearchJournalEntries;
using Anela.Heblo.Domain.Features.Journal;
using Anela.Heblo.Xcc.Persistance;
using FluentAssertions;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Journal;

public class SearchJournalEntriesHandlerTests
{
    private readonly Mock<IJournalRepository> _repositoryMock;
    private readonly SearchJournalEntriesHandler _handler;

    public SearchJournalEntriesHandlerTests()
    {
        _repositoryMock = new Mock<IJournalRepository>();
        _handler = new SearchJournalEntriesHandler(_repositoryMock.Object);
    }

    [Fact]
    public async Task SearchByProductCodePrefix_ShouldReturnEntriesWithMatchingPrefix()
    {
        // Arrange
        var request = new SearchJournalEntriesRequest
        {
            ProductCodePrefix = "TON002",
            PageNumber = 1,
            PageSize = 10
        };

        var journalEntry = new JournalEntry
        {
            Id = 1,
            Title = "Test Entry for TON002",
            Content = "This is about TON002 product family",
            EntryDate = DateTime.Now,
            CreatedAt = DateTime.UtcNow,
            ModifiedAt = DateTime.UtcNow,
            CreatedByUserId = "user123"
        };

        // Add product prefix association
        journalEntry.AssociateWithProduct("TON002");

        var pagedResult = new PagedResult<JournalEntry>
        {
            Items = new List<JournalEntry> { journalEntry },
            TotalCount = 1,
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
            .ReturnsAsync(pagedResult);

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.Entries.Should().HaveCount(1);
        result.Entries.First().Title.Should().Be("Test Entry for TON002");
        result.Entries.First().AssociatedProducts.Should().Contain("TON002");

        _repositoryMock.Verify(x => x.SearchEntriesAsync(
            It.IsAny<string?>(),
            It.IsAny<DateTime?>(),
            It.IsAny<DateTime?>(),
            It.Is<string?>(p => p == "TON002"),
            It.IsAny<IReadOnlyCollection<int>?>(),
            It.IsAny<string?>(),
            It.IsAny<int>(),
            It.IsAny<int>(),
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetEntriesByProduct_ShouldFindEntriesForProductWithPrefix()
    {
        // This test verifies that when searching for product "TON002030",
        // it should find entries associated with prefix "TON002"

        // Arrange
        var productCode = "TON002030";
        var journalEntry = new JournalEntry
        {
            Id = 1,
            Title = "Note about TON002 family",
            Content = "This applies to all TON002 products",
            EntryDate = DateTime.Now,
            CreatedAt = DateTime.UtcNow,
            ModifiedAt = DateTime.UtcNow,
            CreatedByUserId = "user123"
        };

        // Entry is associated with product prefix "TON002"
        journalEntry.AssociateWithProduct("TON002");

        // Test that the product code starts with the prefix
        productCode.StartsWith("TON002").Should().BeTrue();

        // The repository should find this entry when searching for TON002030
        // because TON002030 starts with TON002
        journalEntry.ProductAssociations.Should().HaveCount(1);
        journalEntry.ProductAssociations.First().ProductCodePrefix.Should().Be("TON002");
    }

    [Fact]
    public async Task SearchByProductCodePrefix_ShouldReturnMatchingEntry()
    {
        // Arrange - test single prefix search
        var request = new SearchJournalEntriesRequest
        {
            ProductCodePrefix = "TON002",
            PageNumber = 1,
            PageSize = 10
        };

        var entry = CreateJournalEntryWithProductPrefix("TON002", "Entry for TON002");

        var pagedResult = new PagedResult<JournalEntry>
        {
            Items = new List<JournalEntry> { entry },
            TotalCount = 1,
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
            .ReturnsAsync(pagedResult);

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.Entries.Should().HaveCount(1);
        result.Entries.First().Title.Should().Be("Entry for TON002");
    }

    private JournalEntry CreateJournalEntryWithProductPrefix(string prefix, string title)
    {
        var entry = new JournalEntry
        {
            Id = Random.Shared.Next(1, 1000),
            Title = title,
            Content = $"Content for {prefix}",
            EntryDate = DateTime.Now,
            CreatedAt = DateTime.UtcNow,
            ModifiedAt = DateTime.UtcNow,
            CreatedByUserId = "user123"
        };
        entry.AssociateWithProduct(prefix);
        return entry;
    }

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
        // Arrange: place "needle" near the middle of a long content string
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
}
```

- [ ] **Step 2: Confirm no `JournalSearchCriteria` references remain anywhere**

Run: `grep -rn "JournalQueryCriteria\|JournalSearchCriteria" backend/src backend/test`
Expected: empty output.

- [ ] **Step 3: Build the test project**

Run: `dotnet build backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --nologo`
Expected: **PASS** with zero new warnings.

- [ ] **Step 4: Run Journal tests**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~Journal" --nologo`
Expected: **PASS**. Same number of tests as the Task 0 baseline.

- [ ] **Step 5: Commit the test update**

```bash
git add backend/test/Anela.Heblo.Tests/Features/Journal/SearchJournalEntriesHandlerTests.cs
git commit -m "test(journal): match SearchEntriesAsync per-parameter

After the criteria-type removal, mocks must match the 11-parameter
signature. The single Verify call that asserted ProductCodePrefix
flows through is preserved as a per-parameter It.Is matcher at
position 4 (productCodePrefix)."
```

---

## Task 7: Final acceptance gates

**Files:** none (verification only).

- [ ] **Step 1: Full solution build**

Run: `dotnet build backend/Anela.Heblo.sln --nologo`
Expected: **PASS** with zero new warnings (spec NFR-2). If new warnings appear, investigate; do not suppress.

- [ ] **Step 2: Formatter clean**

Run: `dotnet format backend/Anela.Heblo.sln --verify-no-changes`
Expected: exit 0 (no formatting drift). If it reports diffs, run `dotnet format backend/Anela.Heblo.sln` and commit the formatting fix as a separate `chore: dotnet format` commit.

- [ ] **Step 3: Full Journal test suite**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~Journal" --nologo`
Expected: **PASS**. Total count equal to Task 0 baseline.

- [ ] **Step 4: Spec FR-1 acceptance — criteria types are gone**

Run: `grep -rn "JournalQueryCriteria\|JournalSearchCriteria" backend/src backend/test`
Expected: **empty output**.

- [ ] **Step 5: Arch-review NFR-2 acceptance — Domain ↛ Application**

Run: `grep -R "Anela.Heblo.Application" backend/src/Anela.Heblo.Domain`
Expected: **empty output**.

- [ ] **Step 6: Spec FR-7 acceptance — OpenAPI surface unchanged**

`GetJournalEntriesRequest`, `SearchJournalEntriesRequest`, and all response DTOs in `backend/src/Anela.Heblo.Application/Features/Journal/Contracts/` were not modified by this refactor. Sanity check:

Run: `git diff --name-only main...HEAD -- backend/src/Anela.Heblo.Application/Features/Journal/Contracts/`
Expected: **empty output**. (If any file in `Contracts/` appears here, you've drifted from the spec — investigate before continuing.)

- [ ] **Step 7: Optional — NFR-1 SQL spot check**

The arch review recommends a one-time SQL spot-check to confirm the EF-translated query is byte-identical. This is optional unless the reviewer asks for evidence. To do it:

Enable EF Core SQL logging temporarily in `appsettings.Development.json` (`"Microsoft.EntityFrameworkCore.Database.Command": "Information"`), hit `GET /api/journal?pageNumber=1&pageSize=20` and the corresponding search endpoint with `tagIds = [1,2,3]`, copy the SQL output, then diff against a run from the previous commit. They MUST be byte-identical (apart from query parameter values).

Do **not** commit any change to `appsettings.Development.json` for this check — discard local changes after verifying.

---

## Self-Review

### Spec coverage

| Spec section | Covered by | Notes |
|---|---|---|
| FR-1 (delete Domain criteria types) | Task 2 | Confirmed by Task 7 step 4 grep |
| FR-2 (new `IJournalRepository` signatures) | Task 1 | Exact code in Step 1 |
| FR-3 (`JournalRepository` Persistence impl) | Task 3 | Sort/filter/include behavior preserved verbatim |
| FR-4 (simplify `GetJournalEntriesHandler`) | Task 4 | Full file replacement; no criteria allocation |
| FR-5 (simplify `SearchJournalEntriesHandler`) | Task 5 | Named arguments per arch-review amendment 1 |
| FR-6 (update unit tests) | Task 6 | Per-parameter `It.Is<string?>(p => p == "TON002")` at position 4 |
| FR-7 (external behavior unchanged) | Task 7 step 6 | Contracts dir untouched |
| NFR-1 (performance unchanged) | Task 7 step 7 | Optional SQL spot check |
| NFR-2 (architectural conformance) | Task 7 steps 1, 2, 5 | Build + format clean + Domain ↛ Application grep |
| NFR-3 (test coverage) | Task 7 step 3 | Test count equal to baseline |
| NFR-4 (backward compatibility) | Task 7 step 6 | Contracts dir untouched |
| Arch-review amendment 1 (named args at 11-param site) | Task 5 step 1 | All 11 params passed by name |
| Arch-review amendment 2 (`IReadOnlyCollection<int>?` for tagIds) | Task 1 step 1 | Matches interface, implicit conversion from `List<int>?` at call site |
| Arch-review amendment 3 (Domain csproj grep) | Task 7 step 5 | Explicit grep verification |

### Placeholder scan

No `TBD`, `TODO`, `implement later`, or "similar to Task N" placeholders. Every code step contains complete code. Every command step has expected output. Every file path is absolute within the repo.

### Type consistency

- Parameter order on `SearchEntriesAsync` is **identical** in the interface (Task 1), the implementation (Task 3), the handler call site (Task 5, named), and the test mocks (Task 6, positional). The test mocks rely on positional match; double-check by reading Task 3 and Task 6 side-by-side: `searchText, dateFrom, dateTo, productCodePrefix, tagIds, createdByUserId, pageNumber, pageSize, sortBy, sortDirection, cancellationToken` — same order in both. The `It.Is<string?>(p => p == "TON002")` in the `Verify(...)` call sits at the 4th position, which corresponds to `productCodePrefix`. ✓
- `IReadOnlyCollection<int>?` used in interface (Task 1), implementation (Task 3), and test mocks (Task 6). Handler passes `request.TagIds` which is `List<int>?` — implicit conversion is fine because `List<T>` implements `IReadOnlyCollection<T>`. ✓
- `PagedResult<JournalEntry>` shape (`Items`, `TotalCount`, `PageNumber`, `PageSize`) is unchanged across all tasks. ✓
- Method names (`GetEntriesAsync`, `SearchEntriesAsync`, `GetEntriesByProductAsync`, `GetJournalIndicatorsAsync`) are consistent across interface and implementation. ✓
