# Fix Journal List Sort by Author (CreatedByUsername) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make the Journal list "Autor" header actually sort by `CreatedByUsername` in the backend repository (currently silently falls back to `EntryDate`), remove the dead `createdat` sort branch, and emit a structured warning when the frontend sends an unknown `sortBy` so future contract drift is observable.

**Architecture:** Pure backend correctness fix inside the Journal vertical slice. Consolidate the two duplicated sort switches in `JournalRepository` (used by `GetEntriesAsync` and `SearchEntriesAsync`) into one `private static ApplySort` helper. New switch handles `"title"`, `"createdbyusername"` (with `EntryDate DESC` tiebreaker), default (`EntryDate DESC`), and logs `LogWarning("Unknown sort key {SortBy} requested on {Repository}", sortBy, nameof(JournalRepository))` for non-empty unrecognized values. Whitespace and null/empty stay silent. No frontend change, no migration, no contract change.

**Tech Stack:** .NET 8, EF Core, xUnit, FluentAssertions, Moq, `Microsoft.EntityFrameworkCore.InMemory` for repository integration tests.

---

## File Structure

**Modified:**
- `backend/src/Anela.Heblo.Persistence/Catalog/Journal/JournalRepository.cs` — remove `"createdat"` from both switch sites; replace both with a single call to a new `private static ApplySort` helper; add the helper at the bottom of the class.
- `backend/test/Anela.Heblo.Tests/Features/Journal/JournalRepositoryIntegrationTests.cs` — add sort-by-author tests, unknown-key warning tests, null/empty/whitespace silent-default tests, and one `SearchEntriesAsync` mirror test.

**Created:** None.

**No changes to:** Domain (`JournalEntry`), application handlers, DTOs (`GetJournalEntriesRequest`, `SearchJournalEntriesRequest`), interface (`IJournalRepository`), frontend, EF migrations, DI registration.

---

## Task 1: Add failing tests for sort by `CreatedByUsername` (asc, desc, case-insensitive key, tiebreaker)

**Files:**
- Modify: `backend/test/Anela.Heblo.Tests/Features/Journal/JournalRepositoryIntegrationTests.cs`

- [ ] **Step 1: Add helper factory for sort tests at the bottom of the class**

Open `JournalRepositoryIntegrationTests.cs` and add this private helper just below the existing `CreateEntryWithFamily` method (around line 309):

```csharp
private static JournalEntry CreateEntryWithAuthor(
    string author,
    DateTime entryDate,
    string title)
{
    return new JournalEntry
    {
        Title = title,
        Content = $"Content authored by {author}",
        EntryDate = entryDate,
        CreatedAt = DateTime.UtcNow,
        ModifiedAt = DateTime.UtcNow,
        CreatedByUserId = "test-user-id",
        CreatedByUsername = author
    };
}
```

This factory mirrors `CreateEntryWithFamily` (same shape, no product association), and explicitly sets `CreatedByUsername` — the column the new sort branch reads.

- [ ] **Step 2: Add the failing test for ascending author sort**

Add this test method to the `JournalRepositoryIntegrationTests` class (place it after the existing `GetJournalIndicatorsAsync_WithRecentEntry_FlagsHasRecentEntries` test, before the private helpers section):

```csharp
[Fact]
public async Task GetEntriesAsync_SortsByCreatedByUsername_Ascending()
{
    // Arrange
    var alice = CreateEntryWithAuthor("alice", DateTime.Today.AddDays(-1), "Alice entry");
    var carol = CreateEntryWithAuthor("carol", DateTime.Today.AddDays(-2), "Carol entry");
    var bob = CreateEntryWithAuthor("bob", DateTime.Today.AddDays(-3), "Bob entry");

    await _context.Set<JournalEntry>().AddRangeAsync(alice, carol, bob);
    await _context.SaveChangesAsync();

    // Act
    var result = await _repository.GetEntriesAsync(
        pageNumber: 1,
        pageSize: 10,
        sortBy: "createdByUsername",
        sortDirection: "ASC");

    // Assert
    result.Items.Select(x => x.CreatedByUsername)
        .Should()
        .ContainInOrder("alice", "bob", "carol");
}
```

- [ ] **Step 3: Add the failing test for descending author sort**

Add immediately below the previous test:

```csharp
[Fact]
public async Task GetEntriesAsync_SortsByCreatedByUsername_Descending()
{
    // Arrange
    var alice = CreateEntryWithAuthor("alice", DateTime.Today.AddDays(-1), "Alice entry");
    var carol = CreateEntryWithAuthor("carol", DateTime.Today.AddDays(-2), "Carol entry");
    var bob = CreateEntryWithAuthor("bob", DateTime.Today.AddDays(-3), "Bob entry");

    await _context.Set<JournalEntry>().AddRangeAsync(alice, carol, bob);
    await _context.SaveChangesAsync();

    // Act
    var result = await _repository.GetEntriesAsync(
        pageNumber: 1,
        pageSize: 10,
        sortBy: "createdByUsername",
        sortDirection: "DESC");

    // Assert
    result.Items.Select(x => x.CreatedByUsername)
        .Should()
        .ContainInOrder("carol", "bob", "alice");
}
```

- [ ] **Step 4: Add the failing test for case-insensitive `sortBy` key matching**

```csharp
[Fact]
public async Task GetEntriesAsync_SortsByCreatedByUsername_AcceptsAnyCasing()
{
    // Arrange
    var alice = CreateEntryWithAuthor("alice", DateTime.Today.AddDays(-1), "Alice entry");
    var bob = CreateEntryWithAuthor("bob", DateTime.Today.AddDays(-2), "Bob entry");

    await _context.Set<JournalEntry>().AddRangeAsync(alice, bob);
    await _context.SaveChangesAsync();

    // Act — UPPER, mixed, lower — all must produce the same ordering.
    var upper = await _repository.GetEntriesAsync(1, 10, "CREATEDBYUSERNAME", "ASC");
    var mixed = await _repository.GetEntriesAsync(1, 10, "CreatedByUsername", "ASC");
    var lower = await _repository.GetEntriesAsync(1, 10, "createdbyusername", "ASC");

    // Assert
    upper.Items.Select(x => x.CreatedByUsername).Should().ContainInOrder("alice", "bob");
    mixed.Items.Select(x => x.CreatedByUsername).Should().ContainInOrder("alice", "bob");
    lower.Items.Select(x => x.CreatedByUsername).Should().ContainInOrder("alice", "bob");
}
```

- [ ] **Step 5: Add the failing test for `EntryDate DESC` tiebreaker when two authors are identical**

```csharp
[Fact]
public async Task GetEntriesAsync_SortByCreatedByUsername_TiebreaksByEntryDateDesc()
{
    // Arrange — two entries by "alice" on different dates; expect newer first within the bucket.
    var aliceOlder = CreateEntryWithAuthor("alice", DateTime.Today.AddDays(-5), "Alice older");
    var aliceNewer = CreateEntryWithAuthor("alice", DateTime.Today.AddDays(-1), "Alice newer");
    var bob = CreateEntryWithAuthor("bob", DateTime.Today.AddDays(-3), "Bob entry");

    await _context.Set<JournalEntry>().AddRangeAsync(aliceOlder, aliceNewer, bob);
    await _context.SaveChangesAsync();

    // Act
    var result = await _repository.GetEntriesAsync(1, 10, "createdByUsername", "ASC");

    // Assert — "alice" bucket first (asc), newer entry before older entry within the bucket.
    result.Items.Select(x => x.Title)
        .Should()
        .ContainInOrder("Alice newer", "Alice older", "Bob entry");
}
```

- [ ] **Step 6: Run the new tests to verify they all FAIL**

Run from the repo root:

```bash
cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~JournalRepositoryIntegrationTests.GetEntriesAsync_SortsByCreatedByUsername_Ascending|FullyQualifiedName~JournalRepositoryIntegrationTests.GetEntriesAsync_SortsByCreatedByUsername_Descending|FullyQualifiedName~JournalRepositoryIntegrationTests.GetEntriesAsync_SortsByCreatedByUsername_AcceptsAnyCasing|FullyQualifiedName~JournalRepositoryIntegrationTests.GetEntriesAsync_SortByCreatedByUsername_TiebreaksByEntryDateDesc" \
  --no-restore --nologo --verbosity minimal
```

Expected: 4 tests run, 4 fail. The failures will show `Expected ... to contain in order ... but got ordering by EntryDate DESC` (because the current default-arm code orders by `EntryDate` descending whenever `sortBy` does not match `"title"` or `"createdat"`).

If a test passes here, the test is wrong — investigate before continuing.

- [ ] **Step 7: Commit the failing tests**

```bash
git add backend/test/Anela.Heblo.Tests/Features/Journal/JournalRepositoryIntegrationTests.cs
git commit -m "test: add failing tests for journal sort by createdByUsername"
```

---

## Task 2: Make Task 1 tests pass — extract `ApplySort` helper and add the `createdbyusername` branch

**Files:**
- Modify: `backend/src/Anela.Heblo.Persistence/Catalog/Journal/JournalRepository.cs`

- [ ] **Step 1: Replace both inline sort switches with calls to a new helper**

Open `backend/src/Anela.Heblo.Persistence/Catalog/Journal/JournalRepository.cs`.

**Replace lines 43–55** (the `// Sorting` block in `GetEntriesAsync`) with:

```csharp
            // Sorting
            query = ApplySort(query, sortBy, sortDirection, _logger);
```

**Replace lines 134–146** (the `// Sorting` block in `SearchEntriesAsync`) with the identical call:

```csharp
            // Sorting
            query = ApplySort(query, sortBy, sortDirection, _logger);
```

Note: After both replacements the line numbers above will shift. Make the changes one at a time and locate the second `// Sorting` block by content, not line number.

- [ ] **Step 2: Add the `ApplySort` helper at the bottom of the class**

Add this method as the last method inside the `JournalRepository` class, just before the closing `}` of the class (around line 227 in the original file, now shifted). The helper intentionally omits the unknown-key warning — that arrives in Task 4.

```csharp
        private static IQueryable<JournalEntry> ApplySort(
            IQueryable<JournalEntry> query,
            string? sortBy,
            string sortDirection,
            ILogger logger)
        {
            var ascending = string.Equals(sortDirection, "ASC", StringComparison.OrdinalIgnoreCase);

            if (string.IsNullOrWhiteSpace(sortBy))
            {
                return ApplyDefaultSort(query, ascending);
            }

            return sortBy.ToLowerInvariant() switch
            {
                "title" => ascending
                    ? query.OrderBy(x => x.Title)
                    : query.OrderByDescending(x => x.Title),

                "createdbyusername" => ascending
                    ? query.OrderBy(x => x.CreatedByUsername).ThenByDescending(x => x.EntryDate)
                    : query.OrderByDescending(x => x.CreatedByUsername).ThenByDescending(x => x.EntryDate),

                _ => ApplyDefaultSort(query, ascending),
            };
        }

        private static IQueryable<JournalEntry> ApplyDefaultSort(
            IQueryable<JournalEntry> query,
            bool ascending)
        {
            return ascending
                ? query.OrderBy(x => x.EntryDate)
                : query.OrderByDescending(x => x.EntryDate);
        }
```

Notes on this code:
- `ToLowerInvariant()` (not `ToLower()`) — required by analyzer CA1304 and the spec amendment.
- `string.IsNullOrWhiteSpace(sortBy)` is checked before the switch, satisfying FR-3 acceptance criterion 1 ("null, empty, or whitespace"). Whitespace must not log a warning.
- `"createdat"` is intentionally not present — that branch was dead and the spec FR-2 requires removing it.
- The `logger` parameter is unused for now (Task 4 will use it). Leaving it in the signature here avoids a churning interface change between tasks.

- [ ] **Step 3: Re-run the Task 1 tests to verify they all PASS**

```bash
cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~JournalRepositoryIntegrationTests.GetEntriesAsync_SortsByCreatedByUsername_Ascending|FullyQualifiedName~JournalRepositoryIntegrationTests.GetEntriesAsync_SortsByCreatedByUsername_Descending|FullyQualifiedName~JournalRepositoryIntegrationTests.GetEntriesAsync_SortsByCreatedByUsername_AcceptsAnyCasing|FullyQualifiedName~JournalRepositoryIntegrationTests.GetEntriesAsync_SortByCreatedByUsername_TiebreaksByEntryDateDesc" \
  --nologo --verbosity minimal
```

Expected: 4 tests pass.

- [ ] **Step 4: Run the rest of the existing Journal repository tests to confirm no regression**

```bash
cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~JournalRepositoryIntegrationTests" \
  --nologo --verbosity minimal
```

Expected: all `JournalRepositoryIntegrationTests` tests pass (both the new ones and the pre-existing `GetEntriesByProductAsync_*` and `GetJournalIndicatorsAsync_*` tests).

If any pre-existing test fails, the consolidation was wrong — read its assertion, compare to the new `ApplySort` behavior, fix the helper, and rerun. Do not modify the pre-existing test.

- [ ] **Step 5: Commit**

```bash
git add backend/src/Anela.Heblo.Persistence/Catalog/Journal/JournalRepository.cs
git commit -m "feat: add createdByUsername sort and consolidate journal sort switch"
```

---

## Task 3: Add failing tests for unknown-key warning and silent default for empty/whitespace `sortBy`

**Files:**
- Modify: `backend/test/Anela.Heblo.Tests/Features/Journal/JournalRepositoryIntegrationTests.cs`

- [ ] **Step 1: Add the failing test asserting a warning IS logged for a non-empty unrecognized `sortBy`**

Append to `JournalRepositoryIntegrationTests` (just below the Task 1 tests, before the private helpers section):

```csharp
[Fact]
public async Task GetEntriesAsync_UnknownSortBy_LogsWarningWithStructuredProperty()
{
    // Arrange
    await _context.Set<JournalEntry>().AddAsync(
        CreateEntryWithAuthor("alice", DateTime.Today, "Any entry"));
    await _context.SaveChangesAsync();

    // Act — "tags" is not handled; should default-sort AND log a warning.
    var result = await _repository.GetEntriesAsync(1, 10, "tags", "ASC");

    // Assert — call succeeded with the default sort applied.
    result.Items.Should().HaveCount(1);

    // Assert — exactly one Warning was logged, message references the sortBy value.
    _loggerMock.Verify(
        x => x.Log(
            LogLevel.Warning,
            It.IsAny<EventId>(),
            It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("tags")),
            null,
            It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
        Times.Once);
}
```

- [ ] **Step 2: Add the failing test asserting NO warning is logged for null `sortBy`**

```csharp
[Fact]
public async Task GetEntriesAsync_NullSortBy_DoesNotLogWarning()
{
    // Arrange
    await _context.Set<JournalEntry>().AddAsync(
        CreateEntryWithAuthor("alice", DateTime.Today, "Any entry"));
    await _context.SaveChangesAsync();

    // Act
    var result = await _repository.GetEntriesAsync(1, 10, sortBy: null!, sortDirection: "ASC");

    // Assert
    result.Items.Should().HaveCount(1);
    _loggerMock.Verify(
        x => x.Log(
            LogLevel.Warning,
            It.IsAny<EventId>(),
            It.IsAny<It.IsAnyType>(),
            It.IsAny<Exception?>(),
            It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
        Times.Never);
}
```

Note: `GetEntriesAsync` declares `string sortBy` (not `string?`) — the existing interface signature does not annotate nullability. Passing `null!` exercises the runtime null path that `string.IsNullOrWhiteSpace` is designed to handle. This is intentional behavior coverage, not a contract change.

- [ ] **Step 3: Add the failing test asserting NO warning is logged for empty `sortBy`**

```csharp
[Fact]
public async Task GetEntriesAsync_EmptySortBy_DoesNotLogWarning()
{
    // Arrange
    await _context.Set<JournalEntry>().AddAsync(
        CreateEntryWithAuthor("alice", DateTime.Today, "Any entry"));
    await _context.SaveChangesAsync();

    // Act
    var result = await _repository.GetEntriesAsync(1, 10, sortBy: "", sortDirection: "ASC");

    // Assert
    result.Items.Should().HaveCount(1);
    _loggerMock.Verify(
        x => x.Log(
            LogLevel.Warning,
            It.IsAny<EventId>(),
            It.IsAny<It.IsAnyType>(),
            It.IsAny<Exception?>(),
            It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
        Times.Never);
}
```

- [ ] **Step 4: Add the failing test asserting NO warning is logged for whitespace-only `sortBy`**

```csharp
[Fact]
public async Task GetEntriesAsync_WhitespaceSortBy_DoesNotLogWarning()
{
    // Arrange
    await _context.Set<JournalEntry>().AddAsync(
        CreateEntryWithAuthor("alice", DateTime.Today, "Any entry"));
    await _context.SaveChangesAsync();

    // Act
    var result = await _repository.GetEntriesAsync(1, 10, sortBy: "   ", sortDirection: "ASC");

    // Assert
    result.Items.Should().HaveCount(1);
    _loggerMock.Verify(
        x => x.Log(
            LogLevel.Warning,
            It.IsAny<EventId>(),
            It.IsAny<It.IsAnyType>(),
            It.IsAny<Exception?>(),
            It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
        Times.Never);
}
```

- [ ] **Step 5: Run the four new tests to verify the warning-emitting test FAILS and the three silent tests PASS**

```bash
cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~JournalRepositoryIntegrationTests.GetEntriesAsync_UnknownSortBy_LogsWarningWithStructuredProperty|FullyQualifiedName~JournalRepositoryIntegrationTests.GetEntriesAsync_NullSortBy_DoesNotLogWarning|FullyQualifiedName~JournalRepositoryIntegrationTests.GetEntriesAsync_EmptySortBy_DoesNotLogWarning|FullyQualifiedName~JournalRepositoryIntegrationTests.GetEntriesAsync_WhitespaceSortBy_DoesNotLogWarning" \
  --nologo --verbosity minimal
```

Expected:
- `GetEntriesAsync_UnknownSortBy_LogsWarningWithStructuredProperty` **FAILS** with `Expected invocation on the mock once, but was 0 times` — because Task 2's helper does not log yet.
- `GetEntriesAsync_NullSortBy_DoesNotLogWarning` **PASSES** (already silent).
- `GetEntriesAsync_EmptySortBy_DoesNotLogWarning` **PASSES** (already silent).
- `GetEntriesAsync_WhitespaceSortBy_DoesNotLogWarning` **PASSES** (already silent — `string.IsNullOrWhiteSpace` is checked before the switch).

If any of the three silent-default tests fails, Task 2's whitespace guard is missing or in the wrong place — fix Task 2's helper before continuing.

- [ ] **Step 6: Commit the new tests (one failing, three passing)**

```bash
git add backend/test/Anela.Heblo.Tests/Features/Journal/JournalRepositoryIntegrationTests.cs
git commit -m "test: add tests for unknown sort key warning and silent default"
```

---

## Task 4: Wire the unknown-key warning log

**Files:**
- Modify: `backend/src/Anela.Heblo.Persistence/Catalog/Journal/JournalRepository.cs`

- [ ] **Step 1: Add a warning-emitting default arm to `ApplySort`**

Open `JournalRepository.cs`. Modify the `ApplySort` helper added in Task 2: change the `_ =>` arm of the inner switch from `ApplyDefaultSort(query, ascending)` to a call to a new helper that both logs and defaults.

The full updated helper block (replace the existing `ApplySort` body and add the new `ApplyDefaultSortWithWarning` method directly below `ApplyDefaultSort`):

```csharp
        private static IQueryable<JournalEntry> ApplySort(
            IQueryable<JournalEntry> query,
            string? sortBy,
            string sortDirection,
            ILogger logger)
        {
            var ascending = string.Equals(sortDirection, "ASC", StringComparison.OrdinalIgnoreCase);

            if (string.IsNullOrWhiteSpace(sortBy))
            {
                return ApplyDefaultSort(query, ascending);
            }

            return sortBy.ToLowerInvariant() switch
            {
                "title" => ascending
                    ? query.OrderBy(x => x.Title)
                    : query.OrderByDescending(x => x.Title),

                "createdbyusername" => ascending
                    ? query.OrderBy(x => x.CreatedByUsername).ThenByDescending(x => x.EntryDate)
                    : query.OrderByDescending(x => x.CreatedByUsername).ThenByDescending(x => x.EntryDate),

                _ => ApplyDefaultSortWithWarning(query, ascending, sortBy, logger),
            };
        }

        private static IQueryable<JournalEntry> ApplyDefaultSort(
            IQueryable<JournalEntry> query,
            bool ascending)
        {
            return ascending
                ? query.OrderBy(x => x.EntryDate)
                : query.OrderByDescending(x => x.EntryDate);
        }

        private static IQueryable<JournalEntry> ApplyDefaultSortWithWarning(
            IQueryable<JournalEntry> query,
            bool ascending,
            string sortBy,
            ILogger logger)
        {
            logger.LogWarning(
                "Unknown sort key {SortBy} requested on {Repository}",
                sortBy,
                nameof(JournalRepository));

            return ApplyDefaultSort(query, ascending);
        }
```

Notes:
- The original `sortBy` (not the lowercased value) is logged. This preserves the caller's casing in Application Insights — useful when correlating with the frontend log.
- The log message uses structured properties `{SortBy}` and `{Repository}` — never string-interpolate into the template (that would defeat App Insights filtering and create a theoretical log-injection vector for control characters).
- The warning fires only inside the `_` arm of the switch, which is unreachable when `sortBy` is null/empty/whitespace (guarded above) or matches a known case. This is the precise FR-3 contract.

- [ ] **Step 2: Run all four warning-path tests to verify they now PASS**

```bash
cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~JournalRepositoryIntegrationTests.GetEntriesAsync_UnknownSortBy_LogsWarningWithStructuredProperty|FullyQualifiedName~JournalRepositoryIntegrationTests.GetEntriesAsync_NullSortBy_DoesNotLogWarning|FullyQualifiedName~JournalRepositoryIntegrationTests.GetEntriesAsync_EmptySortBy_DoesNotLogWarning|FullyQualifiedName~JournalRepositoryIntegrationTests.GetEntriesAsync_WhitespaceSortBy_DoesNotLogWarning" \
  --nologo --verbosity minimal
```

Expected: all four pass.

- [ ] **Step 3: Run the full `JournalRepositoryIntegrationTests` class to confirm zero regressions**

```bash
cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~JournalRepositoryIntegrationTests" \
  --nologo --verbosity minimal
```

Expected: every test in the class passes.

- [ ] **Step 4: Commit**

```bash
git add backend/src/Anela.Heblo.Persistence/Catalog/Journal/JournalRepository.cs
git commit -m "feat: log warning when journal sort key is unrecognized"
```

---

## Task 5: Mirror test for `SearchEntriesAsync` to lock in the shared helper

This single test ensures future maintainers who add another sort key cannot re-introduce duplication between `GetEntriesAsync` and `SearchEntriesAsync` without a test signal.

**Files:**
- Modify: `backend/test/Anela.Heblo.Tests/Features/Journal/JournalRepositoryIntegrationTests.cs`

- [ ] **Step 1: Add the `SearchEntriesAsync` mirror test**

Append to `JournalRepositoryIntegrationTests` (with the other new tests):

```csharp
[Fact]
public async Task SearchEntriesAsync_SortsByCreatedByUsername_Ascending()
{
    // Arrange — same setup as GetEntriesAsync_SortsByCreatedByUsername_Ascending.
    var alice = CreateEntryWithAuthor("alice", DateTime.Today.AddDays(-1), "Alice entry");
    var carol = CreateEntryWithAuthor("carol", DateTime.Today.AddDays(-2), "Carol entry");
    var bob = CreateEntryWithAuthor("bob", DateTime.Today.AddDays(-3), "Bob entry");

    await _context.Set<JournalEntry>().AddRangeAsync(alice, carol, bob);
    await _context.SaveChangesAsync();

    // Act — search path with no filters; sort by author ascending.
    var result = await _repository.SearchEntriesAsync(
        searchText: null,
        dateFrom: null,
        dateTo: null,
        productCodePrefix: null,
        tagIds: null,
        createdByUserId: null,
        pageNumber: 1,
        pageSize: 10,
        sortBy: "createdByUsername",
        sortDirection: "ASC");

    // Assert
    result.Items.Select(x => x.CreatedByUsername)
        .Should()
        .ContainInOrder("alice", "bob", "carol");
}
```

- [ ] **Step 2: Run the mirror test to verify it PASSES**

```bash
cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~JournalRepositoryIntegrationTests.SearchEntriesAsync_SortsByCreatedByUsername_Ascending" \
  --nologo --verbosity minimal
```

Expected: passes immediately — both methods share `ApplySort` after Task 2, so no further code change is needed. If it fails, one call-site was not migrated to the helper — go back and check the `// Sorting` replacement in `SearchEntriesAsync`.

- [ ] **Step 3: Commit**

```bash
git add backend/test/Anela.Heblo.Tests/Features/Journal/JournalRepositoryIntegrationTests.cs
git commit -m "test: mirror journal author sort coverage on SearchEntriesAsync"
```

---

## Task 6: Final validation — build, format, full test pass

**Files:** None modified by this task. Validation only.

- [ ] **Step 1: Build the backend solution**

```bash
cd backend && dotnet build --nologo --verbosity minimal
```

Expected: build succeeds with zero errors. Treat any new warning from CA1304 (`ToLower` culture) or CA1305 (`ToLowerInvariant` culture-related) as a regression — Task 2 used `ToLowerInvariant` precisely to avoid them.

- [ ] **Step 2: Verify formatting is clean**

```bash
cd backend && dotnet format --verify-no-changes --no-restore
```

Expected: command exits with code 0 ("no changes required"). If it reports changes, run `dotnet format` without `--verify-no-changes`, review the diff (it must only touch lines this PR already changed — if it touches unrelated files, stop and investigate), then `git add` and `git commit -m "chore: dotnet format"`.

- [ ] **Step 3: Run the entire backend test suite once**

```bash
cd backend && dotnet test --nologo --verbosity minimal
```

Expected: every test passes. The new tests increase the Journal-repository test count by 9 (4 sort assertions + 4 logging assertions + 1 mirror) and do not modify any pre-existing test.

- [ ] **Step 4: Confirm no frontend changes were needed**

```bash
git diff --stat origin/main...HEAD -- frontend/
```

Expected: empty output. If anything appears here, an unrelated edit slipped in — back it out before the PR.

- [ ] **Step 5: Confirm the dead `createdat` branch is gone**

Using Grep against the worktree:

```
Grep tool — pattern: "createdat"  path: backend/src/Anela.Heblo.Persistence/Catalog/Journal/JournalRepository.cs  -i: true
```

Expected: no matches. (FR-2 acceptance criterion 1.)

Also confirm no frontend reference exists:

```
Grep tool — pattern: "createdat"  path: frontend/src/  glob: "*.{ts,tsx}"
```

Expected: no matches related to journal sort. (Other unrelated `createdAt` data fields may exist in the frontend; ignore matches that are property access on DTOs, not sort-key strings. The specific sort-key string `"createdat"` or `createdAt` passed as a column to `SortableHeader` must not exist for any journal column.)

- [ ] **Step 6: No commit (validation only)**

This task introduces no code changes. Move on to PR creation per the standard finish-feature flow.

---

## Spec coverage map

| Spec requirement | Covered by |
|---|---|
| FR-1 ascending sort by `CreatedByUsername` | Task 1 Step 2, Task 2 Step 2 (`"createdbyusername"` arm) |
| FR-1 descending sort by `CreatedByUsername` | Task 1 Step 3, Task 2 Step 2 (`"createdbyusername"` desc arm) |
| FR-1 case-insensitive key match | Task 1 Step 4, Task 2 Step 2 (`ToLowerInvariant`) |
| FR-1 `EntryDate DESC` tiebreaker | Task 1 Step 5, Task 2 Step 2 (`ThenByDescending(EntryDate)`) |
| FR-2 remove `"createdat"` branch | Task 2 Step 2 (omitted from new switch), Task 6 Step 5 (grep verification) |
| FR-3 warning on non-empty unknown key | Task 3 Step 1, Task 4 Step 1 (`ApplyDefaultSortWithWarning`) |
| FR-3 silent default for null `sortBy` | Task 3 Step 2, Task 2 Step 2 (`IsNullOrWhiteSpace` guard) |
| FR-3 silent default for empty `sortBy` | Task 3 Step 3, Task 2 Step 2 |
| FR-3 silent default for whitespace `sortBy` | Task 3 Step 4, Task 2 Step 2 |
| FR-3 structured `{SortBy}` property in warning | Task 4 Step 1 (template uses `{SortBy}` not interpolation) |
| NFR-1 database-side `ORDER BY` (no in-memory sort) | EF Core LINQ translation of `OrderBy(x => x.CreatedByUsername)`; no `AsEnumerable`/`ToList` inserted ahead of `OrderBy` |
| NFR-1 optional index | Out-of-scope per arch-review Amendment 5; deferred to post-deployment measurement |
| NFR-2 no new endpoints / no PII in logs | No new endpoints. Warning logs only the `sortBy` value (UI-supplied column key) + repository name |
| NFR-3 backward compatibility | Task 2 Step 4 (existing tests still pass), default-arm fallback unchanged for any non-author non-title value |
| NFR-4 structured property for App Insights | Task 4 Step 1 (`{SortBy}` template placeholder) |
| Spec consolidation of duplicated switch | Task 2 Step 1 (both `// Sorting` blocks replaced), Task 5 (mirror test covers the shared path) |
| Frontend: no change required | Task 6 Step 4 (git diff shows no frontend changes) |

## Self-review notes

- All test names and method names are consistent across tasks (`ApplySort`, `ApplyDefaultSort`, `ApplyDefaultSortWithWarning`; `CreateEntryWithAuthor`; test method casing matches `GetEntriesAsync_SortsByCreatedByUsername_*`).
- No placeholders; every step shows the exact code or command.
- The plan stays inside the spec scope — no index migration, no controller change, no DTO change, no frontend edit.
- Arch-review amendments 1–4 are honored: `string sortDirection` (not `bool`), `ToLowerInvariant`, `IsNullOrWhiteSpace`, mandatory `SearchEntriesAsync` mirror.
- Amendment 5 (defer index decision) is honored by Task 6 Step 1's NFR-1 note and the omission of any migration task.
