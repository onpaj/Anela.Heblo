# Fulltext Search for Meeting Tasks — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a search box to the Meeting Tasks list page that filters by Subject and Summary (with opt-in full-transcript search) using PostgreSQL `ILike` via EF Core.

**Architecture:** Add `SearchText` and `SearchInTranscript` parameters through the vertical slice (Request → Handler → Repository). The handler passes both fields unchanged to the repository, which applies `EF.Functions.ILike` filters after the existing status filter. The frontend debounces input (300 ms) and adds both params to the React Query key for automatic refetching.

**Tech Stack:** .NET 8, EF Core with Npgsql provider, xUnit, FluentAssertions, Moq; React 18, TypeScript, React Query (@tanstack/react-query), Tailwind CSS

---

## File Map

| File | Change |
|------|--------|
| `backend/src/Anela.Heblo.Application/Features/MeetingTasks/UseCases/GetTranscriptList/GetTranscriptListRequest.cs` | Add `SearchText` and `SearchInTranscript` properties |
| `backend/src/Anela.Heblo.Domain/Features/MeetingTasks/IMeetingTranscriptRepository.cs` | Extend `GetListAsync` signature with 2 new params |
| `backend/src/Anela.Heblo.Persistence/MeetingTasks/MeetingTranscriptRepository.cs` | Implement ILIKE filtering in `GetListAsync` |
| `backend/src/Anela.Heblo.Application/Features/MeetingTasks/UseCases/GetTranscriptList/GetTranscriptListHandler.cs` | Pass new params to repo, update log |
| `backend/test/Anela.Heblo.Tests/Features/MeetingTasks/GetTranscriptListHandlerTests.cs` | Fix mock setup + add 3 new search tests |
| `backend/test/Anela.Heblo.Tests/Application/MeetingTasks/GetTranscriptListHandlerIsManagerTests.cs` | Fix 4 mock calls with updated signature |
| `backend/test/Anela.Heblo.Tests/Features/MeetingTasks/MeetingTranscriptRepositoryTests.cs` | Fix 2 `GetListAsync` calls with updated signature |
| `frontend/src/api/hooks/useMeetingTasks.ts` | Extend `useMeetingTasksList` with 2 new params |
| `frontend/src/components/pages/automation/MeetingTasksPage.tsx` | Add search state, debounce, UI |

---

### Task 1: Add search fields to `GetTranscriptListRequest`

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/MeetingTasks/UseCases/GetTranscriptList/GetTranscriptListRequest.cs`

- [ ] **Step 1: Replace the request class content**

Replace the entire file with:

```csharp
using MediatR;

namespace Anela.Heblo.Application.Features.MeetingTasks.UseCases.GetTranscriptList;

public class GetTranscriptListRequest : IRequest<GetTranscriptListResponse>
{
    public string? StatusFilter { get; set; }
    public string? SearchText { get; set; }
    public bool SearchInTranscript { get; set; } = false;
    public int PageNumber { get; set; } = 1;
    public int PageSize { get; set; } = 20;
}
```

- [ ] **Step 2: Verify the file compiles**

```bash
cd /Users/pajgrtondrej/conductor/workspaces/Anela.Heblo/managua/backend
dotnet build --no-restore 2>&1 | tail -20
```

Expected: build errors about `GetListAsync` argument count — that's fine for now, confirms the property change is live.

- [ ] **Step 3: Commit**

```bash
cd /Users/pajgrtondrej/conductor/workspaces/Anela.Heblo/managua
git add backend/src/Anela.Heblo.Application/Features/MeetingTasks/UseCases/GetTranscriptList/GetTranscriptListRequest.cs
git commit -m "feat(meeting-tasks): add SearchText and SearchInTranscript to GetTranscriptListRequest"
```

---

### Task 2: Extend the repository interface

**Files:**
- Modify: `backend/src/Anela.Heblo.Domain/Features/MeetingTasks/IMeetingTranscriptRepository.cs`

- [ ] **Step 1: Update the `GetListAsync` signature**

Replace only the `GetListAsync` method declaration inside the interface — keep all other methods unchanged:

```csharp
Task<(List<MeetingTranscript> Items, int TotalCount)> GetListAsync(
    MeetingTranscriptStatus? statusFilter,
    string? searchText,
    bool searchInTranscript,
    bool isManager,
    string? userEmail,
    int page,
    int pageSize,
    CancellationToken ct = default);
```

The full file should now read:

```csharp
namespace Anela.Heblo.Domain.Features.MeetingTasks;

public interface IMeetingTranscriptRepository
{
    Task<MeetingTranscript?> GetByIdAsync(Guid id, CancellationToken ct = default);

    Task<(List<MeetingTranscript> Items, int TotalCount)> GetListAsync(
        MeetingTranscriptStatus? statusFilter,
        string? searchText,
        bool searchInTranscript,
        bool isManager,
        string? userEmail,
        int page,
        int pageSize,
        CancellationToken ct = default);

    Task<bool> ExistsByPlaudIdAsync(string plaudRecordingId, CancellationToken ct = default);

    Task AddAsync(MeetingTranscript transcript, CancellationToken ct = default);

    Task SetAccessAsync(
        MeetingTranscript transcript,
        MeetingAccessLevel level,
        IReadOnlyList<MeetingAccessGrant> newGrants,
        CancellationToken ct = default);

    /// <summary>
    /// Removes all Pending tasks from the transcript and replaces them with
    /// <paramref name="newTasks"/>. Approved and Rejected tasks are preserved.
    /// </summary>
    Task ReplacePendingTasksAsync(
        MeetingTranscript transcript,
        IReadOnlyList<ProposedTask> newTasks,
        CancellationToken ct = default);

    Task SaveChangesAsync(CancellationToken ct = default);
}
```

- [ ] **Step 2: Commit**

```bash
cd /Users/pajgrtondrej/conductor/workspaces/Anela.Heblo/managua
git add backend/src/Anela.Heblo.Domain/Features/MeetingTasks/IMeetingTranscriptRepository.cs
git commit -m "feat(meeting-tasks): extend IMeetingTranscriptRepository.GetListAsync with search params"
```

---

### Task 3: Implement ILIKE search in the repository

**Files:**
- Modify: `backend/src/Anela.Heblo.Persistence/MeetingTasks/MeetingTranscriptRepository.cs`

- [ ] **Step 1: Update the `GetListAsync` implementation**

Replace the `GetListAsync` method with:

```csharp
public async Task<(List<MeetingTranscript> Items, int TotalCount)> GetListAsync(
    MeetingTranscriptStatus? statusFilter,
    string? searchText,
    bool searchInTranscript,
    bool isManager,
    string? userEmail,
    int page,
    int pageSize,
    CancellationToken ct = default)
{
    var query = _context.MeetingTranscripts.AsQueryable();

    if (statusFilter.HasValue)
        query = query.Where(x => x.Status == statusFilter.Value);

    if (!string.IsNullOrWhiteSpace(searchText))
    {
        var pattern = $"%{searchText.Trim()}%";
        if (searchInTranscript)
        {
            query = query.Where(x =>
                EF.Functions.ILike(x.Subject, pattern) ||
                EF.Functions.ILike(x.Summary, pattern) ||
                EF.Functions.ILike(x.RawTranscript, pattern));
        }
        else
        {
            query = query.Where(x =>
                EF.Functions.ILike(x.Subject, pattern) ||
                EF.Functions.ILike(x.Summary, pattern));
        }
    }

    if (!isManager)
    {
        var email = (userEmail ?? string.Empty).ToLowerInvariant();
        query = query.Where(x =>
            x.AccessLevel == MeetingAccessLevel.Public ||
            (x.AccessLevel == MeetingAccessLevel.Restricted &&
             x.AccessGrants.Any(g => g.UserEmail.ToLower() == email)));
    }

    var totalCount = await query.CountAsync(ct);

    var items = await query
        .Include(x => x.Tasks)
        .Include(x => x.AccessGrants)
        .OrderByDescending(x => x.PlaudCreatedAt)
        .Skip((page - 1) * pageSize)
        .Take(pageSize)
        .ToListAsync(ct);

    return (items, totalCount);
}
```

`EF.Functions.ILike` is provided by the `Npgsql.EntityFrameworkCore.PostgreSQL` package already referenced in the persistence project. No new `using` directives are needed — `EF` is in `Microsoft.EntityFrameworkCore` which is already imported.

- [ ] **Step 2: Commit**

```bash
cd /Users/pajgrtondrej/conductor/workspaces/Anela.Heblo/managua
git add backend/src/Anela.Heblo.Persistence/MeetingTasks/MeetingTranscriptRepository.cs
git commit -m "feat(meeting-tasks): implement ILike search filter in MeetingTranscriptRepository"
```

---

### Task 4: Update the handler and fix all broken tests

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/MeetingTasks/UseCases/GetTranscriptList/GetTranscriptListHandler.cs`
- Modify: `backend/test/Anela.Heblo.Tests/Features/MeetingTasks/GetTranscriptListHandlerTests.cs`
- Modify: `backend/test/Anela.Heblo.Tests/Application/MeetingTasks/GetTranscriptListHandlerIsManagerTests.cs`
- Modify: `backend/test/Anela.Heblo.Tests/Features/MeetingTasks/MeetingTranscriptRepositoryTests.cs`

#### 4a — Update the handler

- [ ] **Step 1: Update the log line and the repository call in `GetTranscriptListHandler.cs`**

Replace the `Handle` method body. The full method should look like:

```csharp
public async Task<GetTranscriptListResponse> Handle(GetTranscriptListRequest request, CancellationToken cancellationToken)
{
    _logger.LogInformation(
        "Getting meeting transcript list — StatusFilter: {StatusFilter}, SearchText: {SearchText}, SearchInTranscript: {SearchInTranscript}, PageNumber: {PageNumber}, PageSize: {PageSize}",
        request.StatusFilter, request.SearchText, request.SearchInTranscript, request.PageNumber, request.PageSize);

    MeetingTranscriptStatus? statusFilter = null;
    if (!string.IsNullOrWhiteSpace(request.StatusFilter)
        && Enum.TryParse<MeetingTranscriptStatus>(request.StatusFilter, ignoreCase: true, out var parsed))
    {
        statusFilter = parsed;
    }

    var isManager = _accessGuard.IsManager();
    var userEmail = _currentUserService.GetCurrentUser().Email;

    var (items, totalCount) = await _repository.GetListAsync(
        statusFilter,
        request.SearchText,
        request.SearchInTranscript,
        isManager,
        userEmail,
        request.PageNumber,
        request.PageSize,
        cancellationToken);

    var dtos = items.Select(t => new MeetingTranscriptDto
    {
        Id = t.Id,
        PlaudRecordingId = t.PlaudRecordingId,
        PlaudCreatedAt = t.PlaudCreatedAt,
        Subject = t.Subject,
        Summary = t.Summary,
        Status = t.Status.ToString(),
        ReceivedAt = t.ReceivedAt,
        ReviewedAt = t.ReviewedAt,
        ReviewedByUser = t.ReviewedByUser,
        TaskCount = t.Tasks.Count,
        ApprovedTaskCount = t.Tasks.Count(x => x.Status == ProposedTaskStatus.Approved),
        RejectedTaskCount = t.Tasks.Count(x => x.Status == ProposedTaskStatus.Rejected),
        AccessLevel = t.AccessLevel.ToString(),
        Tasks = new()
    }).ToList();

    return new GetTranscriptListResponse
    {
        Items = dtos,
        TotalCount = totalCount,
        PageNumber = request.PageNumber,
        PageSize = request.PageSize
    };
}
```

#### 4b — Fix `GetTranscriptListHandlerTests.cs`

- [ ] **Step 2: Update the mock `Setup` to match the new 8-parameter signature**

In `Handle_ReturnsPagedResults`, replace the `_repositoryMock.Setup(...)` block:

```csharp
_repositoryMock
    .Setup(r => r.GetListAsync(
        It.IsAny<MeetingTranscriptStatus?>(),
        It.IsAny<string?>(),
        It.IsAny<bool>(),
        It.IsAny<bool>(),
        It.IsAny<string?>(),
        It.IsAny<int>(),
        It.IsAny<int>(),
        It.IsAny<CancellationToken>()))
    .ReturnsAsync((new List<MeetingTranscript> { transcript }, 1));
```

- [ ] **Step 3: Update the `Verify` call**

Replace the existing `Verify`:

```csharp
_repositoryMock.Verify(
    r => r.GetListAsync(null, null, false, true, null, 1, 20, It.IsAny<CancellationToken>()),
    Times.Once);
```

#### 4c — Fix `GetTranscriptListHandlerIsManagerTests.cs`

- [ ] **Step 4: Update the manager test — Setup and Verify**

In `Handle_PassesIsManagerTrue_AndEmail_ToRepository_WhenManager`, replace both the `.Setup(...)` and `.Verify(...)` calls:

```csharp
// Setup:
_repositoryMock
    .Setup(x => x.GetListAsync(null, null, false, true, "manager@test.com", 1, 20, It.IsAny<CancellationToken>()))
    .ReturnsAsync((new List<MeetingTranscript>(), 0));

// Verify:
_repositoryMock.Verify(
    x => x.GetListAsync(null, null, false, true, "manager@test.com", 1, 20, It.IsAny<CancellationToken>()),
    Times.Once);
```

- [ ] **Step 5: Update the non-manager test — Setup and Verify**

In `Handle_PassesIsManagerFalse_AndEmail_ToRepository_WhenNonManager`, replace both the `.Setup(...)` and `.Verify(...)` calls:

```csharp
// Setup:
_repositoryMock
    .Setup(x => x.GetListAsync(null, null, false, false, "user@test.com", 1, 20, It.IsAny<CancellationToken>()))
    .ReturnsAsync((new List<MeetingTranscript>(), 0));

// Verify:
_repositoryMock.Verify(
    x => x.GetListAsync(null, null, false, false, "user@test.com", 1, 20, It.IsAny<CancellationToken>()),
    Times.Once);
```

#### 4d — Fix `MeetingTranscriptRepositoryTests.cs`

- [ ] **Step 6: Fix `GetListAsync_FiltersByStatus_PaginatesAndOrdersByPlaudCreatedAtDescending`**

In this test, replace the `GetListAsync` call at the bottom of the arrange/act section (currently using named params) with:

```csharp
var (items, totalCount) = await _repository.GetListAsync(
    statusFilter: MeetingTranscriptStatus.PendingReview,
    searchText: null,
    searchInTranscript: false,
    isManager: true,
    userEmail: null,
    page: 1,
    pageSize: 10);
```

- [ ] **Step 7: Fix `GetListAsync_WithoutStatusFilter_ReturnsAll`**

Replace the `GetListAsync` call in this test:

```csharp
var (items, totalCount) = await _repository.GetListAsync(
    statusFilter: null,
    searchText: null,
    searchInTranscript: false,
    isManager: true,
    userEmail: null,
    page: 1,
    pageSize: 10);
```

#### 4e — Verify everything compiles and existing tests pass

- [ ] **Step 8: Build and run affected tests**

```bash
cd /Users/pajgrtondrej/conductor/workspaces/Anela.Heblo/managua/backend
dotnet build
```

Expected: **Build succeeded** with 0 errors.

```bash
dotnet test --filter "FullyQualifiedName~MeetingTasks" --no-build
```

Expected: all existing MeetingTasks tests pass.

- [ ] **Step 9: Commit**

```bash
cd /Users/pajgrtondrej/conductor/workspaces/Anela.Heblo/managua
git add \
  backend/src/Anela.Heblo.Application/Features/MeetingTasks/UseCases/GetTranscriptList/GetTranscriptListHandler.cs \
  backend/test/Anela.Heblo.Tests/Features/MeetingTasks/GetTranscriptListHandlerTests.cs \
  backend/test/Anela.Heblo.Tests/Application/MeetingTasks/GetTranscriptListHandlerIsManagerTests.cs \
  backend/test/Anela.Heblo.Tests/Features/MeetingTasks/MeetingTranscriptRepositoryTests.cs
git commit -m "feat(meeting-tasks): pass search params through handler; fix test signatures"
```

---

### Task 5: Add handler tests for search behaviour

**Files:**
- Modify: `backend/test/Anela.Heblo.Tests/Features/MeetingTasks/GetTranscriptListHandlerTests.cs`

These tests verify that the handler passes `SearchText` and `SearchInTranscript` to the repository exactly as received — no transformation, no filtering at the handler layer.

- [ ] **Step 1: Add a reusable mock setup helper**

At the top of the test class (after the constructor), add a private helper that avoids repeating the 8-param `It.IsAny` setup across multiple tests:

```csharp
private void SetupRepositoryReturnsEmpty()
{
    _repositoryMock
        .Setup(r => r.GetListAsync(
            It.IsAny<MeetingTranscriptStatus?>(),
            It.IsAny<string?>(),
            It.IsAny<bool>(),
            It.IsAny<bool>(),
            It.IsAny<string?>(),
            It.IsAny<int>(),
            It.IsAny<int>(),
            It.IsAny<CancellationToken>()))
        .ReturnsAsync((new List<MeetingTranscript>(), 0));
}
```

- [ ] **Step 2: Write failing test — search text is forwarded**

Add this test (it will not compile until the class has the right imports, but the logic should fail at the `Verify` until Task 4 is done):

```csharp
[Fact]
public async Task Handle_ForwardsSearchText_ToRepository()
{
    // Arrange
    SetupRepositoryReturnsEmpty();
    var request = new GetTranscriptListRequest
    {
        SearchText = "sprint",
        PageNumber = 1,
        PageSize = 20
    };

    // Act
    var result = await _handler.Handle(request, CancellationToken.None);

    // Assert
    result.Success.Should().BeTrue();
    _repositoryMock.Verify(
        r => r.GetListAsync(null, "sprint", false, true, null, 1, 20, It.IsAny<CancellationToken>()),
        Times.Once);
}
```

- [ ] **Step 3: Write failing test — searchInTranscript is forwarded**

```csharp
[Fact]
public async Task Handle_ForwardsSearchInTranscriptTrue_ToRepository()
{
    // Arrange
    SetupRepositoryReturnsEmpty();
    var request = new GetTranscriptListRequest
    {
        SearchText = "akce",
        SearchInTranscript = true,
        PageNumber = 1,
        PageSize = 20
    };

    // Act
    await _handler.Handle(request, CancellationToken.None);

    // Assert
    _repositoryMock.Verify(
        r => r.GetListAsync(null, "akce", true, true, null, 1, 20, It.IsAny<CancellationToken>()),
        Times.Once);
}
```

- [ ] **Step 4: Write failing test — null search text when omitted**

```csharp
[Fact]
public async Task Handle_PassesNullSearchText_WhenSearchTextNotSet()
{
    // Arrange
    SetupRepositoryReturnsEmpty();
    var request = new GetTranscriptListRequest { PageNumber = 1, PageSize = 20 };

    // Act
    await _handler.Handle(request, CancellationToken.None);

    // Assert
    _repositoryMock.Verify(
        r => r.GetListAsync(null, null, false, true, null, 1, 20, It.IsAny<CancellationToken>()),
        Times.Once);
}
```

- [ ] **Step 5: Run tests — confirm new tests pass**

```bash
cd /Users/pajgrtondrej/conductor/workspaces/Anela.Heblo/managua/backend
dotnet test --filter "FullyQualifiedName~GetTranscriptListHandlerTests" --no-build
```

Expected: all 4 tests in `GetTranscriptListHandlerTests` pass.

- [ ] **Step 6: Commit**

```bash
cd /Users/pajgrtondrej/conductor/workspaces/Anela.Heblo/managua
git add backend/test/Anela.Heblo.Tests/Features/MeetingTasks/GetTranscriptListHandlerTests.cs
git commit -m "test(meeting-tasks): add handler tests for SearchText and SearchInTranscript forwarding"
```

---

### Task 6: Run full backend test suite

- [ ] **Step 1: Run all backend tests**

```bash
cd /Users/pajgrtondrej/conductor/workspaces/Anela.Heblo/managua/backend
dotnet test
```

Expected: **all tests pass**.

- [ ] **Step 2: Run dotnet format**

```bash
cd /Users/pajgrtondrej/conductor/workspaces/Anela.Heblo/managua/backend
dotnet format
```

Expected: no changes (or minor whitespace fixes — commit any changes it makes).

- [ ] **Step 3: Commit format fixes if any**

```bash
cd /Users/pajgrtondrej/conductor/workspaces/Anela.Heblo/managua
git diff --quiet || git commit -am "chore: dotnet format"
```

---

### Task 7: Extend `useMeetingTasksList` hook

**Files:**
- Modify: `frontend/src/api/hooks/useMeetingTasks.ts`

- [ ] **Step 1: Update the function signature and body**

Replace the existing `useMeetingTasksList` function (lines 125–144) with:

```typescript
export function useMeetingTasksList(
  statusFilter?: string,
  searchText?: string,
  searchInTranscript: boolean = false,
  page: number = 1,
  pageSize: number = 20,
) {
  return useQuery<TranscriptListResponse>({
    queryKey: [
      ...QUERY_KEYS.meetingTasks,
      statusFilter ?? "",
      searchText ?? "",
      searchInTranscript,
      page,
      pageSize,
    ],
    refetchOnMount: "always",
    queryFn: () => {
      const params = new URLSearchParams();
      if (statusFilter) params.append("statusFilter", statusFilter);
      if (searchText) {
        params.append("searchText", searchText);
        if (searchInTranscript) params.append("searchInTranscript", "true");
      }
      params.append("pageNumber", String(page));
      params.append("pageSize", String(pageSize));
      return fetchJson<TranscriptListResponse>(
        `/api/meeting-tasks?${params.toString()}`,
        { method: "GET", headers: { Accept: "application/json" } },
      );
    },
  });
}
```

Note: `searchInTranscript` is only sent to the backend when `searchText` is also set — no point sending it when there is no search term.

- [ ] **Step 2: Commit**

```bash
cd /Users/pajgrtondrej/conductor/workspaces/Anela.Heblo/managua
git add frontend/src/api/hooks/useMeetingTasks.ts
git commit -m "feat(meeting-tasks): extend useMeetingTasksList with searchText and searchInTranscript params"
```

---

### Task 8: Add search UI to `MeetingTasksPage`

**Files:**
- Modify: `frontend/src/components/pages/automation/MeetingTasksPage.tsx`

- [ ] **Step 1: Add `useEffect` to the import**

Replace the existing import line:

```typescript
import React, { useState } from "react";
```

With:

```typescript
import React, { useState, useEffect } from "react";
```

- [ ] **Step 2: Add search state and debounce logic**

Inside `MeetingTasksPage`, after the existing state declarations (`statusFilter`, `page`), add:

```typescript
const [searchInput, setSearchInput] = useState("");
const [searchInTranscript, setSearchInTranscript] = useState(false);
const [debouncedSearch, setDebouncedSearch] = useState("");

useEffect(() => {
  const timer = setTimeout(() => setDebouncedSearch(searchInput), 300);
  return () => clearTimeout(timer);
}, [searchInput]);

useEffect(() => {
  setPage(1);
}, [debouncedSearch, searchInTranscript]);
```

- [ ] **Step 3: Update the hook call**

Replace:

```typescript
const { data, isLoading, refetch, isFetching } = useMeetingTasksList(statusFilter, page, PAGE_SIZE);
```

With:

```typescript
const { data, isLoading, refetch, isFetching } = useMeetingTasksList(
  statusFilter,
  debouncedSearch || undefined,
  searchInTranscript,
  page,
  PAGE_SIZE,
);
```

(`debouncedSearch || undefined` converts the empty string to `undefined`, so the hook doesn't append `searchText=` to the URL when the field is empty.)

- [ ] **Step 4: Add search input and checkbox to the filter row**

Replace:

```tsx
<div className="flex-shrink-0 mb-3 px-4 sm:px-6 lg:px-8 flex gap-2">
  {filterButton("Vse", undefined)}
  {filterButton("Ke kontrole", "PendingReview" as TranscriptStatus)}
  {filterButton("Schvaleno", "Approved" as TranscriptStatus)}
</div>
```

With:

```tsx
<div className="flex-shrink-0 mb-3 px-4 sm:px-6 lg:px-8 flex flex-wrap items-center gap-2">
  {filterButton("Vse", undefined)}
  {filterButton("Ke kontrole", "PendingReview" as TranscriptStatus)}
  {filterButton("Schvaleno", "Approved" as TranscriptStatus)}
  <input
    type="text"
    value={searchInput}
    onChange={(e) => setSearchInput(e.target.value)}
    placeholder="Hledat..."
    className="px-3 py-1.5 rounded-md text-sm border border-gray-300 focus:outline-none focus:ring-1 focus:ring-indigo-500 focus:border-indigo-500"
  />
  <label className="flex items-center gap-1.5 text-sm text-gray-700 cursor-pointer select-none">
    <input
      type="checkbox"
      checked={searchInTranscript}
      onChange={(e) => setSearchInTranscript(e.target.checked)}
      className="rounded border-gray-300 text-indigo-600 focus:ring-indigo-500"
    />
    Hledat i v prepisu
  </label>
</div>
```

- [ ] **Step 5: Commit**

```bash
cd /Users/pajgrtondrej/conductor/workspaces/Anela.Heblo/managua
git add frontend/src/components/pages/automation/MeetingTasksPage.tsx
git commit -m "feat(meeting-tasks): add search input and transcript toggle to meeting tasks list"
```

---

### Task 9: Frontend build and lint verification

- [ ] **Step 1: Run TypeScript build**

```bash
cd /Users/pajgrtondrej/conductor/workspaces/Anela.Heblo/managua/frontend
npm run build 2>&1 | tail -30
```

Expected: build completes with **0 errors**. TypeScript will catch any type mismatch from the hook signature change.

- [ ] **Step 2: Run lint**

```bash
cd /Users/pajgrtondrej/conductor/workspaces/Anela.Heblo/managua/frontend
npm run lint 2>&1 | tail -20
```

Expected: 0 errors.

- [ ] **Step 3: Commit any lint fixes**

```bash
cd /Users/pajgrtondrej/conductor/workspaces/Anela.Heblo/managua
git diff --quiet || git commit -am "fix: lint fixes"
```

---

## Notes

### Why not test ILIKE in repository integration tests?
The `MeetingTranscriptRepositoryTests` uses EF Core's `UseInMemoryDatabase` provider, which does not support `EF.Functions.ILike` (it's Npgsql-specific SQL). Calling ILike against InMemory would throw `InvalidOperationException` at test time. Coverage for the search filtering is handled at the handler layer (Task 5) by verifying that the correct parameters are forwarded to the repository. The actual SQL generation and case-insensitivity is verified during manual smoke testing against the STG/dev PostgreSQL database.

### LIKE wildcards in search input
`%` and `_` in `searchText` are passed directly into the ILIKE pattern. This is intentional — parameterized queries prevent SQL injection, and wildcard pass-through gives power users more control.
