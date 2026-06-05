# Remove Redundant Soft-Delete Predicates in `JournalRepository` — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Remove five duplicated `!x.IsDeleted` predicates from `JournalRepository.cs` so the global EF Core query filter on `JournalEntry` becomes the single source of truth for soft-delete enforcement.

**Architecture:** Behavior-preserving refactor inside one repository class. `JournalEntryConfiguration` already registers `HasQueryFilter(x => !x.IsDeleted)`, which EF Core appends to every query targeting `DbSet<JournalEntry>` (including the inner DbSet used as a join source). The five `.Where(x => !x.IsDeleted)` / `&& !x.IsDeleted` fragments in `JournalRepository` produce duplicate SQL and create a maintenance trap if a future admin path needs `IgnoreQueryFilters()`. Five regression tests are added first to lock the soft-delete contract before the predicates are removed.

**Tech Stack:** .NET 8, EF Core (PostgreSQL in prod, InMemory in tests), xUnit, FluentAssertions, Moq.

---

## File Structure

**Files modified:**
- `backend/src/Anela.Heblo.Persistence/Catalog/Journal/JournalRepository.cs` — five surgical edits to remove duplicate predicates (lines 26, 40, 90, 172, 188).
- `backend/test/Anela.Heblo.Tests/Features/Journal/JournalRepositoryIntegrationTests.cs` — five new regression tests (one per affected repository method) that insert a soft-deleted entry and assert it is excluded.

**Files NOT modified:**
- `JournalEntryConfiguration.cs` — global filter and index unchanged.
- `IJournalRepository.cs` — public surface unchanged.
- Any MediatR handler, DTO, or migration — none touched.
- `MarketingActionRepository.cs` — same anti-pattern exists at lines 24, 49, 124 but is **out of scope per spec**. Mention in PR description, do not edit.

---

## Conventions for this plan

- Every code edit must compile and pass tests independently — commit after each task.
- Use the existing `JournalEntry` `SoftDelete(string userId, string username)` domain method only when an entry has already been persisted; for new-entry-already-deleted setups in tests, set `IsDeleted = true` directly during construction (the entity already exposes a public `IsDeleted { get; set; }` and the in-memory provider does not enforce the query filter at insert time).
- All tests live in the existing `JournalRepositoryIntegrationTests` class. Do not add a new test file.
- Test naming pattern: `<MethodUnderTest>_WhenEntryIsSoftDeleted_<ExpectedBehavior>`.
- Existing tests (the seven currently in `JournalRepositoryIntegrationTests`) MUST continue to pass with zero edits.

---

## Task 1: Add baseline regression tests for soft-delete exclusion (all five methods)

**Why first:** These tests pass against the *current* code (duplicate predicates) and must continue to pass after each predicate is removed. Adding them first turns FR-6 from "reviewer inspects" into "test enforces". This is a test-only commit; the production code is untouched.

**Files:**
- Modify: `backend/test/Anela.Heblo.Tests/Features/Journal/JournalRepositoryIntegrationTests.cs` (append five `[Fact]` methods before the `CreateEntryWithFamily` helper)

- [ ] **Step 1: Add five regression tests**

Open `backend/test/Anela.Heblo.Tests/Features/Journal/JournalRepositoryIntegrationTests.cs` and insert the five tests below immediately after the existing `GetJournalIndicatorsAsync_WithRecentEntry_FlagsHasRecentEntries` test (line 294) and immediately before the `private JournalEntry CreateEntryWithFamily(...)` helper (line 296):

```csharp
[Fact]
public async Task GetByIdAsync_WhenEntryIsSoftDeleted_ReturnsNull()
{
    // Arrange
    var entry = new JournalEntry
    {
        Title = "Soft-deleted entry",
        Content = "Content",
        EntryDate = DateTime.Today,
        CreatedAt = DateTime.UtcNow,
        ModifiedAt = DateTime.UtcNow,
        CreatedByUserId = "test-user",
        IsDeleted = true,
        DeletedAt = DateTime.UtcNow,
        DeletedByUserId = "test-user"
    };
    await _context.Set<JournalEntry>().AddAsync(entry);
    await _context.SaveChangesAsync();

    // Act
    var result = await _repository.GetByIdAsync(entry.Id);

    // Assert
    result.Should().BeNull("soft-deleted entries must be excluded by the global query filter");
}

[Fact]
public async Task GetEntriesAsync_WhenEntryIsSoftDeleted_ExcludesFromResults()
{
    // Arrange
    var live = new JournalEntry
    {
        Title = "Live entry",
        Content = "Content",
        EntryDate = DateTime.Today,
        CreatedAt = DateTime.UtcNow,
        ModifiedAt = DateTime.UtcNow,
        CreatedByUserId = "test-user"
    };
    var deleted = new JournalEntry
    {
        Title = "Deleted entry",
        Content = "Content",
        EntryDate = DateTime.Today.AddDays(-1),
        CreatedAt = DateTime.UtcNow,
        ModifiedAt = DateTime.UtcNow,
        CreatedByUserId = "test-user",
        IsDeleted = true,
        DeletedAt = DateTime.UtcNow,
        DeletedByUserId = "test-user"
    };
    await _context.Set<JournalEntry>().AddRangeAsync(live, deleted);
    await _context.SaveChangesAsync();

    // Act
    var result = await _repository.GetEntriesAsync(pageNumber: 1, pageSize: 50, sortBy: "entrydate", sortDirection: "DESC");

    // Assert
    result.TotalCount.Should().Be(1);
    result.Items.Should().ContainSingle(e => e.Title == "Live entry");
    result.Items.Should().NotContain(e => e.Title == "Deleted entry");
}

[Fact]
public async Task SearchEntriesAsync_WhenEntryIsSoftDeleted_ExcludesFromResults()
{
    // Arrange
    var live = new JournalEntry
    {
        Title = "Searchable live",
        Content = "matching term",
        EntryDate = DateTime.Today,
        CreatedAt = DateTime.UtcNow,
        ModifiedAt = DateTime.UtcNow,
        CreatedByUserId = "test-user"
    };
    var deleted = new JournalEntry
    {
        Title = "Searchable deleted",
        Content = "matching term",
        EntryDate = DateTime.Today,
        CreatedAt = DateTime.UtcNow,
        ModifiedAt = DateTime.UtcNow,
        CreatedByUserId = "test-user",
        IsDeleted = true,
        DeletedAt = DateTime.UtcNow,
        DeletedByUserId = "test-user"
    };
    await _context.Set<JournalEntry>().AddRangeAsync(live, deleted);
    await _context.SaveChangesAsync();

    // Act
    var result = await _repository.SearchEntriesAsync(
        searchText: "matching",
        dateFrom: null,
        dateTo: null,
        productCodePrefix: null,
        tagIds: null,
        createdByUserId: null,
        pageNumber: 1,
        pageSize: 50,
        sortBy: "entrydate",
        sortDirection: "DESC");

    // Assert
    result.TotalCount.Should().Be(1);
    result.Items.Should().ContainSingle(e => e.Title == "Searchable live");
    result.Items.Should().NotContain(e => e.Title == "Searchable deleted");
}

[Fact]
public async Task GetEntriesByProductAsync_WhenEntryIsSoftDeleted_ExcludesFromResults()
{
    // Arrange
    var live = new JournalEntry
    {
        Title = "Live TON002 entry",
        Content = "Content",
        EntryDate = DateTime.Today,
        CreatedAt = DateTime.UtcNow,
        ModifiedAt = DateTime.UtcNow,
        CreatedByUserId = "test-user"
    };
    live.AssociateWithProduct("TON002");

    var deleted = new JournalEntry
    {
        Title = "Deleted TON002 entry",
        Content = "Content",
        EntryDate = DateTime.Today.AddDays(-1),
        CreatedAt = DateTime.UtcNow,
        ModifiedAt = DateTime.UtcNow,
        CreatedByUserId = "test-user",
        IsDeleted = true,
        DeletedAt = DateTime.UtcNow,
        DeletedByUserId = "test-user"
    };
    deleted.AssociateWithProduct("TON002");

    await _context.Set<JournalEntry>().AddRangeAsync(live, deleted);
    await _context.SaveChangesAsync();

    // Act
    var result = await _repository.GetEntriesByProductAsync("TON002030");

    // Assert
    result.Should().ContainSingle();
    result.Single().Title.Should().Be("Live TON002 entry");
}

[Fact]
public async Task GetJournalIndicatorsAsync_WhenEntryIsSoftDeleted_ExcludesFromCount()
{
    // Arrange — verifies the join source honors the global query filter
    var deleted = new JournalEntry
    {
        Title = "Deleted TON002 entry",
        Content = "Content",
        EntryDate = DateTime.Today,
        CreatedAt = DateTime.UtcNow,
        ModifiedAt = DateTime.UtcNow,
        CreatedByUserId = "test-user",
        IsDeleted = true,
        DeletedAt = DateTime.UtcNow,
        DeletedByUserId = "test-user"
    };
    deleted.AssociateWithProduct("TON002");

    await _context.Set<JournalEntry>().AddAsync(deleted);
    await _context.SaveChangesAsync();

    // Act
    var result = await _repository.GetJournalIndicatorsAsync(new[] { "TON002" });

    // Assert
    result.Should().ContainKey("TON002");
    var indicator = result["TON002"];
    indicator.DirectEntries.Should().Be(0, "soft-deleted entries must not count toward indicators");
    indicator.LastEntryDate.Should().BeNull();
    indicator.HasRecentEntries.Should().BeFalse();
}
```

- [ ] **Step 2: Run the new tests to verify they PASS against current code**

Run from the worktree root:

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~JournalRepositoryIntegrationTests" \
  -v normal
```

Expected: All 12 tests (7 existing + 5 new) PASS. The 5 new ones pass because the current code has explicit `!x.IsDeleted` predicates that already exclude soft-deleted entries — this is the baseline we are protecting.

If any of the five new tests fail at this point, stop. Either the test setup is wrong, or the in-memory provider does not honor `HasQueryFilter` for this scenario (extremely unlikely — EF Core in-memory has supported global filters since 2.1). Re-read the test and the entity configuration before changing anything else.

- [ ] **Step 3: Run `dotnet format` to keep style consistent**

```bash
dotnet format backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj
```

Expected: No changes outside the new test methods.

- [ ] **Step 4: Commit the regression tests**

```bash
git add backend/test/Anela.Heblo.Tests/Features/Journal/JournalRepositoryIntegrationTests.cs
git commit -m "test: lock soft-delete exclusion contract on JournalRepository"
```

---

## Task 2: Remove redundant predicate in `GetByIdAsync` (`JournalRepository.cs:26`)

**Files:**
- Modify: `backend/src/Anela.Heblo.Persistence/Catalog/Journal/JournalRepository.cs:26`
- Test: `backend/test/Anela.Heblo.Tests/Features/Journal/JournalRepositoryIntegrationTests.cs::GetByIdAsync_WhenEntryIsSoftDeleted_ReturnsNull`

- [ ] **Step 1: Apply the edit**

In `backend/src/Anela.Heblo.Persistence/Catalog/Journal/JournalRepository.cs`, replace the body of `GetByIdAsync`.

Before:

```csharp
public override async Task<JournalEntry?> GetByIdAsync(int id, CancellationToken cancellationToken = default)
{
    return await Context.Set<JournalEntry>()
        .Include(x => x.ProductAssociations)
        .Include(x => x.TagAssignments)
            .ThenInclude(x => x.Tag)
        .FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted, cancellationToken);
}
```

After:

```csharp
public override async Task<JournalEntry?> GetByIdAsync(int id, CancellationToken cancellationToken = default)
{
    return await Context.Set<JournalEntry>()
        .Include(x => x.ProductAssociations)
        .Include(x => x.TagAssignments)
            .ThenInclude(x => x.Tag)
        .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
}
```

- [ ] **Step 2: Run the targeted regression test**

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~JournalRepositoryIntegrationTests.GetByIdAsync_WhenEntryIsSoftDeleted_ReturnsNull" \
  -v normal
```

Expected: PASS. Proves the global filter is still excluding soft-deleted rows from `FirstOrDefaultAsync`.

- [ ] **Step 3: Run the full repository test class**

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~JournalRepositoryIntegrationTests" \
  -v normal
```

Expected: All 12 tests PASS.

- [ ] **Step 4: Commit**

```bash
git add backend/src/Anela.Heblo.Persistence/Catalog/Journal/JournalRepository.cs
git commit -m "refactor: drop redundant !IsDeleted predicate in GetByIdAsync"
```

---

## Task 3: Remove redundant predicate in `GetEntriesAsync` (`JournalRepository.cs:40`)

**Files:**
- Modify: `backend/src/Anela.Heblo.Persistence/Catalog/Journal/JournalRepository.cs:40`
- Test: `JournalRepositoryIntegrationTests::GetEntriesAsync_WhenEntryIsSoftDeleted_ExcludesFromResults`

- [ ] **Step 1: Apply the edit**

In `JournalRepository.cs`, delete the `.Where(x => !x.IsDeleted)` line in `GetEntriesAsync`. Keep `.AsQueryable()` — the subsequent `switch` reassigns to `IQueryable<JournalEntry>`, and `.AsQueryable()` preserves the existing chain shape.

Before:

```csharp
var query = Context.Set<JournalEntry>()
    .Include(x => x.ProductAssociations)
    .Include(x => x.TagAssignments)
        .ThenInclude(x => x.Tag)
    .Where(x => !x.IsDeleted)
    .AsQueryable();
```

After:

```csharp
var query = Context.Set<JournalEntry>()
    .Include(x => x.ProductAssociations)
    .Include(x => x.TagAssignments)
        .ThenInclude(x => x.Tag)
    .AsQueryable();
```

- [ ] **Step 2: Run the targeted regression test**

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~JournalRepositoryIntegrationTests.GetEntriesAsync_WhenEntryIsSoftDeleted_ExcludesFromResults" \
  -v normal
```

Expected: PASS. The global filter excludes the deleted entry; `TotalCount` is 1.

- [ ] **Step 3: Run the full repository test class**

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~JournalRepositoryIntegrationTests" \
  -v normal
```

Expected: All 12 tests PASS.

- [ ] **Step 4: Commit**

```bash
git add backend/src/Anela.Heblo.Persistence/Catalog/Journal/JournalRepository.cs
git commit -m "refactor: drop redundant !IsDeleted predicate in GetEntriesAsync"
```

---

## Task 4: Remove redundant predicate in `SearchEntriesAsync` (`JournalRepository.cs:90`)

**Files:**
- Modify: `backend/src/Anela.Heblo.Persistence/Catalog/Journal/JournalRepository.cs:90`
- Test: `JournalRepositoryIntegrationTests::SearchEntriesAsync_WhenEntryIsSoftDeleted_ExcludesFromResults`

- [ ] **Step 1: Apply the edit**

In `JournalRepository.cs`, delete the `.Where(x => !x.IsDeleted)` line in `SearchEntriesAsync`. Keep `.AsQueryable()` for the same reason as Task 3.

Before:

```csharp
var query = Context.Set<JournalEntry>()
    .Include(x => x.ProductAssociations)
    .Include(x => x.TagAssignments)
        .ThenInclude(x => x.Tag)
    .Where(x => !x.IsDeleted)
    .AsQueryable();
```

After:

```csharp
var query = Context.Set<JournalEntry>()
    .Include(x => x.ProductAssociations)
    .Include(x => x.TagAssignments)
        .ThenInclude(x => x.Tag)
    .AsQueryable();
```

- [ ] **Step 2: Run the targeted regression test**

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~JournalRepositoryIntegrationTests.SearchEntriesAsync_WhenEntryIsSoftDeleted_ExcludesFromResults" \
  -v normal
```

Expected: PASS.

- [ ] **Step 3: Run the full repository test class**

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~JournalRepositoryIntegrationTests" \
  -v normal
```

Expected: All 12 tests PASS.

- [ ] **Step 4: Commit**

```bash
git add backend/src/Anela.Heblo.Persistence/Catalog/Journal/JournalRepository.cs
git commit -m "refactor: drop redundant !IsDeleted predicate in SearchEntriesAsync"
```

---

## Task 5: Remove redundant predicate in `GetEntriesByProductAsync` (`JournalRepository.cs:172`)

**Files:**
- Modify: `backend/src/Anela.Heblo.Persistence/Catalog/Journal/JournalRepository.cs:172`
- Test: `JournalRepositoryIntegrationTests::GetEntriesByProductAsync_WhenEntryIsSoftDeleted_ExcludesFromResults`

- [ ] **Step 1: Apply the edit**

In `JournalRepository.cs`, rewrite the `.Where(...)` clause inside `GetEntriesByProductAsync` to keep only the product-association predicate.

Before:

```csharp
public async Task<List<JournalEntry>> GetEntriesByProductAsync(
    string productCode,
    CancellationToken cancellationToken = default)
{
    return await Context.Set<JournalEntry>()
        .Include(x => x.ProductAssociations)
        .Include(x => x.TagAssignments)
            .ThenInclude(x => x.Tag)
        .Where(x => !x.IsDeleted && (x.ProductAssociations.Any(pa => productCode.StartsWith(pa.ProductCodePrefix))))
        .OrderByDescending(x => x.EntryDate)
        .ThenByDescending(x => x.CreatedAt)
        .ToListAsync(cancellationToken);
}
```

After:

```csharp
public async Task<List<JournalEntry>> GetEntriesByProductAsync(
    string productCode,
    CancellationToken cancellationToken = default)
{
    return await Context.Set<JournalEntry>()
        .Include(x => x.ProductAssociations)
        .Include(x => x.TagAssignments)
            .ThenInclude(x => x.Tag)
        .Where(x => x.ProductAssociations.Any(pa => productCode.StartsWith(pa.ProductCodePrefix)))
        .OrderByDescending(x => x.EntryDate)
        .ThenByDescending(x => x.CreatedAt)
        .ToListAsync(cancellationToken);
}
```

- [ ] **Step 2: Run the targeted regression test**

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~JournalRepositoryIntegrationTests.GetEntriesByProductAsync_WhenEntryIsSoftDeleted_ExcludesFromResults" \
  -v normal
```

Expected: PASS.

- [ ] **Step 3: Run the full repository test class** (also covers the five existing `GetEntriesByProductAsync_*` family tests)

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~JournalRepositoryIntegrationTests" \
  -v normal
```

Expected: All 12 tests PASS.

- [ ] **Step 4: Commit**

```bash
git add backend/src/Anela.Heblo.Persistence/Catalog/Journal/JournalRepository.cs
git commit -m "refactor: drop redundant !IsDeleted predicate in GetEntriesByProductAsync"
```

---

## Task 6: Remove redundant predicate in `GetJournalIndicatorsAsync` (`JournalRepository.cs:188`)

**Files:**
- Modify: `backend/src/Anela.Heblo.Persistence/Catalog/Journal/JournalRepository.cs:188`
- Test: `JournalRepositoryIntegrationTests::GetJournalIndicatorsAsync_WhenEntryIsSoftDeleted_ExcludesFromCount`

**Note:** This is the critical join-source change. The global filter on `JournalEntry` applies to any `DbSet<JournalEntry>` reference, including when used as a `Join` inner source. The regression test added in Task 1 verifies this exact behavior.

- [ ] **Step 1: Apply the edit**

In `JournalRepository.cs`, replace `Context.Set<JournalEntry>().Where(je => !je.IsDeleted)` with `Context.Set<JournalEntry>()` inside the `.Join(...)` call.

Before:

```csharp
var directAssociations = await Context.Set<JournalEntryProduct>()
    .Where(jep => productCodeList.Contains(jep.ProductCodePrefix))
    .Join(Context.Set<JournalEntry>().Where(je => !je.IsDeleted),
        jep => jep.JournalEntryId,
        je => je.Id,
        (jep, je) => new { ProductCode = jep.ProductCodePrefix, je.EntryDate, je.CreatedAt })
    .GroupBy(x => x.ProductCode)
    .Select(g => new
    {
        ProductCode = g.Key,
        Count = g.Count(),
        LastEntryDate = g.Max(x => x.EntryDate)
    })
    .ToListAsync(cancellationToken);
```

After:

```csharp
var directAssociations = await Context.Set<JournalEntryProduct>()
    .Where(jep => productCodeList.Contains(jep.ProductCodePrefix))
    .Join(Context.Set<JournalEntry>(),
        jep => jep.JournalEntryId,
        je => je.Id,
        (jep, je) => new { ProductCode = jep.ProductCodePrefix, je.EntryDate, je.CreatedAt })
    .GroupBy(x => x.ProductCode)
    .Select(g => new
    {
        ProductCode = g.Key,
        Count = g.Count(),
        LastEntryDate = g.Max(x => x.EntryDate)
    })
    .ToListAsync(cancellationToken);
```

- [ ] **Step 2: Run the targeted regression test**

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~JournalRepositoryIntegrationTests.GetJournalIndicatorsAsync_WhenEntryIsSoftDeleted_ExcludesFromCount" \
  -v normal
```

Expected: PASS. `indicator.DirectEntries` is 0 and `LastEntryDate` is null even though a `JournalEntryProduct` row exists for `TON002` — proof that the global filter is applied to the join source.

- [ ] **Step 3: Run the three existing `GetJournalIndicatorsAsync_*` tests** to confirm aggregate semantics are unchanged

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~JournalRepositoryIntegrationTests.GetJournalIndicatorsAsync" \
  -v normal
```

Expected: All four `GetJournalIndicatorsAsync_*` tests PASS (`WithMultipleDirectEntries_ReturnsCorrectCount`, `WithNoEntries_ReturnsZeroIndicator`, `WithRecentEntry_FlagsHasRecentEntries`, `WhenEntryIsSoftDeleted_ExcludesFromCount`).

- [ ] **Step 4: Run the full repository test class**

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~JournalRepositoryIntegrationTests" \
  -v normal
```

Expected: All 12 tests PASS.

- [ ] **Step 5: Commit**

```bash
git add backend/src/Anela.Heblo.Persistence/Catalog/Journal/JournalRepository.cs
git commit -m "refactor: drop redundant !IsDeleted predicate in GetJournalIndicatorsAsync join"
```

---

## Task 7: Full validation suite

**Why:** Per `CLAUDE.md` "Validation before completion", every backend change must pass `dotnet build`, `dotnet format`, and the full backend test suite before the task is considered done. No FE / no E2E (refactor is BE-only and contract-preserving).

- [ ] **Step 1: Run `dotnet build` on the whole solution**

```bash
dotnet build Anela.Heblo.sln
```

Expected: Build succeeds with zero new warnings.

- [ ] **Step 2: Run `dotnet format` to verify nothing was missed**

```bash
dotnet format Anela.Heblo.sln --verify-no-changes
```

Expected: No formatting differences. If this fails, run `dotnet format Anela.Heblo.sln` (without `--verify-no-changes`), inspect the diff to confirm it stays within the edited methods, and commit the formatter changes as `style: dotnet format`.

- [ ] **Step 3: Run the full backend test suite**

```bash
dotnet test Anela.Heblo.sln
```

Expected: All tests PASS. Particular attention to anything under `Features/Journal/` and any handler tests that exercise `IJournalRepository` (search the test output for `Journal`).

- [ ] **Step 4 (optional, per FR-6 spot-check): Verify generated SQL contains exactly one `IsDeleted` predicate**

If you want belt-and-braces evidence (the spec lists this as optional, reviewer-level): temporarily enable EF Core query logging in one test by adding to the in-memory options builder:

```csharp
.LogTo(Console.WriteLine, Microsoft.Extensions.Logging.LogLevel.Information)
```

Run the test, scan the logged SQL for the touched method, and confirm `IsDeleted` appears exactly once per `JournalEntries` reference. Revert the logging change before committing — it is debug-only. **Do not commit this change.**

In-memory provider note: EF Core in-memory does not emit relational SQL. To exercise this spot-check meaningfully, prefer running the existing logging hooks on a PostgreSQL connection (out of scope for normal CI). If the visual SQL check is impractical, the regression tests from Task 1 are sufficient — they prove the *behavior*, which is the actual acceptance bar.

- [ ] **Step 5: Skim the final diff**

```bash
git log --oneline main..HEAD
git diff main..HEAD -- backend/src/Anela.Heblo.Persistence/Catalog/Journal/JournalRepository.cs
git diff main..HEAD -- backend/test/Anela.Heblo.Tests/Features/Journal/JournalRepositoryIntegrationTests.cs
```

Expected:
- Six commits (one test commit + five refactor commits).
- `JournalRepository.cs` diff shows exactly five removed predicate fragments and no other changes.
- `JournalRepositoryIntegrationTests.cs` diff shows exactly five added `[Fact]` methods and no other changes.

If the diff contains anything else (re-formatting unrelated lines, removed `.AsQueryable()`, touched `using` directives, edits to `JournalEntryConfiguration.cs`, edits to `IJournalRepository.cs`, edits to handlers), revert it. Per `CLAUDE.md` "Surgical changes": every changed line must trace directly to the request.

---

## PR description requirements

When opening the PR, the description MUST include:

1. **Behavior preservation statement** — "Behavior-preserving refactor. The `HasQueryFilter(x => !x.IsDeleted)` registered at `JournalEntryConfiguration.cs:53` already enforces soft-delete exclusion on every query targeting `DbSet<JournalEntry>`, including join sources. The five `.Where(x => !x.IsDeleted)` / `&& !x.IsDeleted` fragments removed here produced duplicated SQL and obscured the real enforcement point."

2. **Convention note** — "Soft-delete enforcement on `JournalEntry` lives in `JournalEntryConfiguration`. To query soft-deleted rows in the future (e.g., admin/audit views), add a new repository method that calls `IgnoreQueryFilters()` — follow the pattern at `MarketingActionRepository.cs:140`. Do not re-add per-method `!IsDeleted` predicates."

3. **Parallel finding** — "The same anti-pattern exists in `MarketingActionRepository.cs:24,49,124`. Out of scope per the spec; track separately if cleanup is desired."

4. **Link to the spec and architecture review** artifacts uploaded for this branch.

---

## Out of scope (do not do)

- Audit / refactor `MarketingActionRepository.cs` or any other repository.
- Introduce an `includeDeleted` parameter or any `IgnoreQueryFilters()` admin path.
- Touch `JournalEntryConfiguration.cs`, the `(IsDeleted, EntryDate)` composite index, or any migration.
- Refactor unrelated sort logic, paging logic, `ToLower()` search-term handling, or include strategies in `JournalRepository.cs`.
- Add additional tests beyond the five regression tests in Task 1. Coverage uplift is not a goal of this PR.
- Frontend changes. E2E test runs.
