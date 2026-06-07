# Extract Duplicated Journal Sorting Logic Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Eliminate the verbatim duplicated sort-clause `switch` expression in `JournalRepository.GetEntriesAsync` and `JournalRepository.SearchEntriesAsync` by extracting it into a single `private static ApplySorting` helper, while preserving exact external behavior.

**Architecture:** Backend-only refactor inside a single file. Introduce a `private static IQueryable<JournalEntry> ApplySorting(IQueryable<JournalEntry>, string?, string)` helper at the bottom of `JournalRepository`, then replace both inline `switch` blocks (lines 44–55 and 135–146) with a one-line call. Public interface `IJournalRepository` is untouched. Pattern mirrors `IssuedInvoiceRepository.ApplySorting` already established elsewhere in the codebase.

**Tech Stack:** .NET 8, C#, EF Core (`IQueryable<T>` expression trees), xUnit + FluentAssertions for tests, EF Core in-memory provider in the existing `JournalRepositoryIntegrationTests` fixture.

---

## File Structure

**Modified files:**
- `backend/src/Anela.Heblo.Persistence/Catalog/Journal/JournalRepository.cs` — extract helper, replace 2 inline switch blocks with calls to it.
- `backend/test/Anela.Heblo.Tests/Features/Journal/JournalRepositoryIntegrationTests.cs` — add sort-matrix coverage for both `GetEntriesAsync` and `SearchEntriesAsync`.

**No new files.** No new project references. No DI changes. No database migrations. No OpenAPI/TypeScript client regeneration (interface unchanged).

---

## Task 1: Add sort matrix tests for `GetEntriesAsync` (RED)

Write the first set of failing tests that pin down current sort behavior for `GetEntriesAsync`. We do RED-first so we can prove the refactor preserves behavior. These tests must pass on the current (pre-refactor) code — if they fail before we touch `JournalRepository.cs`, we've misunderstood current behavior and must fix the test, not the code.

**Files:**
- Modify: `backend/test/Anela.Heblo.Tests/Features/Journal/JournalRepositoryIntegrationTests.cs`

- [ ] **Step 1.1: Add the sort-matrix theory test for `GetEntriesAsync` plus a seeding helper**

Append the following inside the `JournalRepositoryIntegrationTests` class, before the `private JournalEntry CreateEntryWithFamily(...)` helper near the bottom:

```csharp
    // ---------- Sort matrix tests (FR-1 / FR-4) ----------

    private async Task SeedSortFixtureAsync()
    {
        // Three entries with deliberately distinct Title, CreatedAt, and EntryDate
        // values so each (sortBy, sortDirection) combination produces a unique ordering.
        var alpha = new JournalEntry
        {
            Title = "Alpha",
            Content = "alpha content",
            EntryDate = new DateTime(2024, 1, 1),
            CreatedAt = new DateTime(2024, 3, 1, 0, 0, 0, DateTimeKind.Utc),
            ModifiedAt = new DateTime(2024, 3, 1, 0, 0, 0, DateTimeKind.Utc),
            CreatedByUserId = "test-user"
        };
        var bravo = new JournalEntry
        {
            Title = "Bravo",
            Content = "bravo content",
            EntryDate = new DateTime(2024, 2, 1),
            CreatedAt = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            ModifiedAt = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            CreatedByUserId = "test-user"
        };
        var charlie = new JournalEntry
        {
            Title = "Charlie",
            Content = "charlie content",
            EntryDate = new DateTime(2024, 3, 1),
            CreatedAt = new DateTime(2024, 2, 1, 0, 0, 0, DateTimeKind.Utc),
            ModifiedAt = new DateTime(2024, 2, 1, 0, 0, 0, DateTimeKind.Utc),
            CreatedByUserId = "test-user"
        };

        await _context.Set<JournalEntry>().AddRangeAsync(alpha, bravo, charlie);
        await _context.SaveChangesAsync();
    }

    public static IEnumerable<object[]> SortMatrix()
    {
        // (sortBy, sortDirection, expectedTitlesInOrder)
        // Mapping of sortBy values:
        //   "title"     -> sort by Title
        //   "createdat" -> sort by CreatedAt
        //   anything else (including null and unknown) -> sort by EntryDate
        // Mapping of sortDirection:
        //   "ASC" -> ascending; anything else -> descending
        yield return new object[] { "title", "ASC", new[] { "Alpha", "Bravo", "Charlie" } };
        yield return new object[] { "title", "DESC", new[] { "Charlie", "Bravo", "Alpha" } };
        yield return new object[] { "title", "weird", new[] { "Charlie", "Bravo", "Alpha" } }; // non-ASC -> descending
        yield return new object[] { "createdat", "ASC", new[] { "Bravo", "Charlie", "Alpha" } };
        yield return new object[] { "createdat", "DESC", new[] { "Alpha", "Charlie", "Bravo" } };
        yield return new object[] { "createdat", "weird", new[] { "Alpha", "Charlie", "Bravo" } };
        yield return new object[] { "TITLE", "ASC", new[] { "Alpha", "Bravo", "Charlie" } }; // case-insensitive sortBy
        yield return new object[] { "unknown", "ASC", new[] { "Alpha", "Bravo", "Charlie" } }; // unknown falls back to EntryDate
        yield return new object[] { "unknown", "DESC", new[] { "Charlie", "Bravo", "Alpha" } };
        yield return new object[] { null!, "ASC", new[] { "Alpha", "Bravo", "Charlie" } }; // null falls back to EntryDate
        yield return new object[] { null!, "DESC", new[] { "Charlie", "Bravo", "Alpha" } };
        yield return new object[] { null!, "weird", new[] { "Charlie", "Bravo", "Alpha" } };
    }

    [Theory]
    [MemberData(nameof(SortMatrix))]
    public async Task GetEntriesAsync_AppliesExpectedOrdering(
        string? sortBy, string sortDirection, string[] expectedTitlesInOrder)
    {
        // Arrange
        await SeedSortFixtureAsync();

        // Act
        var result = await _repository.GetEntriesAsync(
            pageNumber: 1,
            pageSize: 10,
            sortBy: sortBy!,
            sortDirection: sortDirection);

        // Assert
        result.Should().NotBeNull();
        result.Items.Select(x => x.Title).Should().ContainInOrder(expectedTitlesInOrder);
        result.TotalCount.Should().Be(expectedTitlesInOrder.Length);
    }
```

- [ ] **Step 1.2: Run the new tests against unmodified production code; expect them to PASS**

Run:

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~JournalRepositoryIntegrationTests.GetEntriesAsync_AppliesExpectedOrdering"
```

Expected: All 12 theory cases PASS. (We are pinning current behavior, not driving new behavior.) If any case fails, the spec/architecture assumptions about current sort semantics are wrong — stop and reconcile with the spec before continuing.

- [ ] **Step 1.3: Commit the new tests as a behavior baseline**

```bash
git add backend/test/Anela.Heblo.Tests/Features/Journal/JournalRepositoryIntegrationTests.cs
git commit -m "test: pin GetEntriesAsync sort matrix as refactor baseline"
```

---

## Task 2: Add sort matrix tests for `SearchEntriesAsync` (RED)

Same coverage applied to `SearchEntriesAsync` so any future regression in either caller is caught — spec FR-4 requires both methods covered.

**Files:**
- Modify: `backend/test/Anela.Heblo.Tests/Features/Journal/JournalRepositoryIntegrationTests.cs`

- [ ] **Step 2.1: Add the second theory test using the same `SortMatrix` member data and the same `SeedSortFixtureAsync` helper**

Add directly below the `GetEntriesAsync_AppliesExpectedOrdering` test:

```csharp
    [Theory]
    [MemberData(nameof(SortMatrix))]
    public async Task SearchEntriesAsync_AppliesExpectedOrdering(
        string? sortBy, string sortDirection, string[] expectedTitlesInOrder)
    {
        // Arrange
        await SeedSortFixtureAsync();

        // Act
        // No filters supplied -> all three seeded rows should come back, ordered by the sort args.
        var result = await _repository.SearchEntriesAsync(
            searchText: null,
            dateFrom: null,
            dateTo: null,
            productCodePrefix: null,
            tagIds: null,
            createdByUserId: null,
            pageNumber: 1,
            pageSize: 10,
            sortBy: sortBy!,
            sortDirection: sortDirection);

        // Assert
        result.Should().NotBeNull();
        result.Items.Select(x => x.Title).Should().ContainInOrder(expectedTitlesInOrder);
        result.TotalCount.Should().Be(expectedTitlesInOrder.Length);
    }
```

- [ ] **Step 2.2: Run the new tests against unmodified production code; expect them to PASS**

Run:

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~JournalRepositoryIntegrationTests.SearchEntriesAsync_AppliesExpectedOrdering"
```

Expected: All 12 theory cases PASS. Same diagnostic rule as Step 1.2 — failure means our model of current behavior is wrong; stop and investigate.

- [ ] **Step 2.3: Commit the second baseline**

```bash
git add backend/test/Anela.Heblo.Tests/Features/Journal/JournalRepositoryIntegrationTests.cs
git commit -m "test: pin SearchEntriesAsync sort matrix as refactor baseline"
```

---

## Task 3: Extract `ApplySorting` and replace both call sites (GREEN)

Now that behavior is pinned by 24 theory cases, perform the refactor. The helper goes at the bottom of the class (after `GetJournalIndicatorsAsync`) to minimize diff noise in the public methods. Both inline `switch` blocks become a single-line call.

**Files:**
- Modify: `backend/src/Anela.Heblo.Persistence/Catalog/Journal/JournalRepository.cs:43-55` (first call site)
- Modify: `backend/src/Anela.Heblo.Persistence/Catalog/Journal/JournalRepository.cs:134-146` (second call site)
- Modify: `backend/src/Anela.Heblo.Persistence/Catalog/Journal/JournalRepository.cs` (append helper before closing brace of class)

- [ ] **Step 3.1: Replace the `GetEntriesAsync` sort block (currently lines 43–55) with a call to the helper**

Find this block:

```csharp
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
```

Replace it with:

```csharp
            // Sorting
            query = ApplySorting(query, sortBy, sortDirection);
```

- [ ] **Step 3.2: Replace the `SearchEntriesAsync` sort block (currently lines 134–146) with a call to the helper**

Find the identical block inside `SearchEntriesAsync`:

```csharp
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
```

Replace it with:

```csharp
            // Sorting
            query = ApplySorting(query, sortBy, sortDirection);
```

- [ ] **Step 3.3: Add the `ApplySorting` helper at the bottom of the class**

Locate the closing brace of `GetJournalIndicatorsAsync` (currently around line 226). Immediately after that closing brace and before the closing brace of the `JournalRepository` class, add:

```csharp

        private static IQueryable<JournalEntry> ApplySorting(
            IQueryable<JournalEntry> query, string? sortBy, string sortDirection) =>
            sortBy?.ToLower() switch
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
```

- [ ] **Step 3.4: Build the persistence project to confirm compilation**

Run:

```bash
dotnet build backend/src/Anela.Heblo.Persistence/Anela.Heblo.Persistence.csproj
```

Expected: Build succeeded, 0 errors. If the build fails because `Anela.Heblo.Persistence.csproj` is not the correct project file path, fall back to a solution-wide build: `dotnet build backend/Anela.Heblo.sln`. Any compilation error means the helper was mis-placed (typical cause: pasted outside the class braces or inside an existing method).

- [ ] **Step 3.5: Run the full Journal test class — all baselines from Tasks 1 and 2 must still pass**

Run:

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~JournalRepositoryIntegrationTests"
```

Expected: All tests PASS, including:
- 5 pre-existing `GetEntriesByProductAsync` / `GetJournalIndicatorsAsync` tests
- 12 `GetEntriesAsync_AppliesExpectedOrdering` cases
- 12 `SearchEntriesAsync_AppliesExpectedOrdering` cases

A failure here proves the refactor changed behavior — diff the helper against the original inline switch and reconcile. Do not modify the tests to make them pass; FR-3 mandates byte-for-byte behavioral equivalence.

- [ ] **Step 3.6: Verify single-source-of-truth invariant (FR-1 acceptance)**

Run:

```bash
grep -n '"title"' backend/src/Anela.Heblo.Persistence/Catalog/Journal/JournalRepository.cs
grep -n '"createdat"' backend/src/Anela.Heblo.Persistence/Catalog/Journal/JournalRepository.cs
```

Expected: Each string appears **exactly once** in the file (inside `ApplySorting`). If either grep returns 2+ hits, one of the inline switches survived — go back to Step 3.1 or 3.2.

- [ ] **Step 3.7: Format and commit the refactor**

Run:

```bash
dotnet format backend/src/Anela.Heblo.Persistence/Anela.Heblo.Persistence.csproj
```

Then commit:

```bash
git add backend/src/Anela.Heblo.Persistence/Catalog/Journal/JournalRepository.cs
git commit -m "refactor: extract duplicated journal sort logic into ApplySorting"
```

---

## Task 4: Final validation gate

Per project `CLAUDE.md`, declare the change done only after the full backend build/format/test gate passes.

- [ ] **Step 4.1: Solution-wide build**

Run:

```bash
dotnet build backend/Anela.Heblo.sln
```

Expected: Build succeeded, 0 errors, 0 new warnings.

- [ ] **Step 4.2: Solution-wide format check**

Run:

```bash
dotnet format backend/Anela.Heblo.sln --verify-no-changes
```

Expected: Exit code 0 (nothing to format). If non-zero, run `dotnet format backend/Anela.Heblo.sln`, review the diff, and commit any whitespace fixes as a separate `chore: dotnet format` commit.

- [ ] **Step 4.3: Run the Journal repository tests one more time**

Run:

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~JournalRepositoryIntegrationTests"
```

Expected: All tests PASS. This is the FR-3 acceptance gate.

- [ ] **Step 4.4: Spot-check the diff for surgical scope**

Run:

```bash
git diff --stat origin/main...HEAD -- backend/src/Anela.Heblo.Persistence/Catalog/Journal/JournalRepository.cs
git diff origin/main...HEAD -- backend/src/Anela.Heblo.Persistence/Catalog/Journal/JournalRepository.cs
```

Expected: only the two switch blocks shrunk to one-liners, plus the appended `ApplySorting` helper. If unrelated formatting, comment, or method-body changes appear, revert them — CLAUDE.md mandates surgical changes.

---

## Spec Coverage Check

| Spec requirement | Task(s) |
|---|---|
| FR-1: Extract `ApplySorting` helper with exact semantics | Task 3 (3.3) |
| FR-1 acceptance: helper is `private static`, returns `IQueryable<JournalEntry>`, uses `.ToLower()`, uses exact-match `"ASC"` | Task 3 (3.3) + Task 3 (3.6) grep check |
| FR-2: Replace both duplicated blocks with call to helper; preserve pipeline position | Task 3 (3.1, 3.2) |
| FR-2 acceptance: both methods invoke `ApplySorting`; sort step keeps its position before pagination | Task 3 (3.1, 3.2) — instructions replace only the labeled "// Sorting" block |
| FR-3: External behavior preserved across all `(sortBy × sortDirection)` combos | Tasks 1, 2 pin behavior; Task 3.5 verifies still passing |
| FR-4: Coverage of `(sortBy, sortDirection)` matrix for both methods | Task 1 (`GetEntriesAsync`), Task 2 (`SearchEntriesAsync`) |
| NFR-1: No performance regression (relaxed per Arch Amendment 1 — expression-tree equivalence) | Helper returns plain `IQueryable<JournalEntry>` with no closure capture (Step 3.3); ordering correctness verified in Task 3.5 |
| NFR-2: No security implications | N/A — purely internal refactor; no input surface change |
| NFR-3: Adding a new sortable column edits one location | Verified structurally by Task 3.6 grep check |
| Arch Amendment 3: helper placed at bottom of class after `GetJournalIndicatorsAsync` | Step 3.3 |
| Out-of-scope items (new columns, enum direction, case-insensitive direction, validation) | Not touched — Task 3 instructions copy the existing switch semantics verbatim |
