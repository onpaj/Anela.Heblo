# Implementation Plan: Remove dead code — `GetJournalIndicatorsAsync` and `JournalIndicatorDto`

## Context for the implementer

This is a pure deletion task. `IJournalRepository.GetJournalIndicatorsAsync` (Domain), its EF Core
implementation in `JournalRepository` (Persistence), the `JournalIndicatorSnapshot` return-value type
(Domain), the never-wired `JournalIndicatorDto` (Application/Contracts), and 4 integration tests that
exist solely to exercise the method are all confirmed dead: zero MediatR handlers, zero MVC
controllers, zero frontend references. `spec.r1.md` and `arch-review.r1.md` both independently
verified this via repo-wide grep. There is no design doc content beyond "delete these files/members
in place" (`Skip Design: true` in the arch review).

**Important line-number caveat** (called out explicitly in `arch-review.r1.md`): the brief's line
numbers were correct as of filing, but you must re-verify them yourself before editing, because:
- Deleting the `RecentEntriesDays` constant (currently `JournalRepository.cs` line 12) shifts every
  subsequent line in that file up by one line before you get to the method body deletion.
- Any other change on this branch could have shifted line numbers further.

Do not blindly trust the line numbers below — re-run the grep/read steps in Step 1 of the task and use
the numbers **you observe**, not the numbers in this document, if they differ.

## Working directory

All paths below are relative to the repo root:
`/home/user/worktrees/feature-3577-Arch-Review-Journal-Getjournalindicatorsasync-And`

---

### task: remove-dead-journal-indicators-code

**Files:**
- Modify: `backend/src/Anela.Heblo.Domain/Features/Journal/IJournalRepository.cs:31-34`
- Delete: `backend/src/Anela.Heblo.Domain/Features/Journal/JournalIndicatorSnapshot.cs`
- Modify: `backend/src/Anela.Heblo.Persistence/Journal/JournalRepository.cs:12` (constant), `:154-202` (method)
- Delete: `backend/src/Anela.Heblo.Application/Features/Journal/Contracts/JournalIndicatorDto.cs`
- Modify: `backend/test/Anela.Heblo.Tests/Features/Journal/JournalRepositoryIntegrationTests.cs:195-294,767-798`

- [ ] **Step 1: Re-verify current line ranges before touching anything**

  Run this from the repo root to confirm every reference to the four dead symbols is still confined
  to the five files named in the spec (no fifth file has appeared since the review was written):

  ```bash
  grep -rn "GetJournalIndicatorsAsync\|JournalIndicatorDto\|JournalIndicatorSnapshot" backend/ --include="*.cs"
  ```

  Expected: every hit is inside one of:
  - `backend/src/Anela.Heblo.Domain/Features/Journal/IJournalRepository.cs`
  - `backend/src/Anela.Heblo.Domain/Features/Journal/JournalIndicatorSnapshot.cs`
  - `backend/src/Anela.Heblo.Persistence/Journal/JournalRepository.cs`
  - `backend/src/Anela.Heblo.Application/Features/Journal/Contracts/JournalIndicatorDto.cs`
  - `backend/test/Anela.Heblo.Tests/Features/Journal/JournalRepositoryIntegrationTests.cs`

  If any hit appears in a different file, STOP — the "zero other callers" premise of this task no
  longer holds, and the spec/arch-review need to be revisited before deleting anything.

  Then read the exact current line numbers for each edit target:

  ```bash
  cat -n backend/src/Anela.Heblo.Domain/Features/Journal/IJournalRepository.cs
  cat -n backend/src/Anela.Heblo.Persistence/Journal/JournalRepository.cs
  grep -n "RecentEntriesDays\|GetJournalIndicatorsAsync\|^    }" backend/src/Anela.Heblo.Persistence/Journal/JournalRepository.cs
  grep -n "\[Fact\]\|GetJournalIndicatorsAsync" backend/test/Anela.Heblo.Tests/Features/Journal/JournalRepositoryIntegrationTests.cs
  ```

  Use the line numbers **you see from these commands** for the edits in Steps 2-5, not the numbers
  quoted below (those reflect the state observed while writing this plan and may have drifted).

  As observed when this plan was written, the exact current text at each location is reproduced in
  the steps below — match on that text (not on line numbers) if line numbers have shifted at all.

- [ ] **Step 2: Remove `GetJournalIndicatorsAsync` from `IJournalRepository`**

  File: `backend/src/Anela.Heblo.Domain/Features/Journal/IJournalRepository.cs`

  Current content (interface has 4 members; observed lines 1-35):

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

          Task<Dictionary<string, JournalIndicatorSnapshot>> GetJournalIndicatorsAsync(
              IEnumerable<string> productCodes,
              CancellationToken cancellationToken = default);
      }
  }
  ```

  Delete the `GetJournalIndicatorsAsync` signature block (the blank line before it plus the 4 lines of
  the method signature), i.e. remove exactly this text:

  ```csharp

          Task<Dictionary<string, JournalIndicatorSnapshot>> GetJournalIndicatorsAsync(
              IEnumerable<string> productCodes,
              CancellationToken cancellationToken = default);
  ```

  so the interface ends with:

  ```csharp
          Task<List<JournalEntry>> GetEntriesByProductAsync(
              string productCode,
              CancellationToken cancellationToken = default);
      }
  }
  ```

  Do not touch the `using` statement or the other three method signatures.

- [ ] **Step 3: Delete `JournalIndicatorSnapshot.cs` entirely**

  ```bash
  rm backend/src/Anela.Heblo.Domain/Features/Journal/JournalIndicatorSnapshot.cs
  ```

  This file's only content is the `readonly record struct JournalIndicatorSnapshot` used solely as the
  return type of the method removed in Step 2.

- [ ] **Step 4: Remove `RecentEntriesDays` constant and `GetJournalIndicatorsAsync` implementation from `JournalRepository`**

  File: `backend/src/Anela.Heblo.Persistence/Journal/JournalRepository.cs`

  4a. Remove the constant field. Current text (observed at line 12, directly under the `_logger` field):

  ```csharp
          private readonly ILogger<JournalRepository> _logger;
          private const int RecentEntriesDays = 30;
  ```

  becomes:

  ```csharp
          private readonly ILogger<JournalRepository> _logger;
  ```

  (delete only the `private const int RecentEntriesDays = 30;` line — keep the `_logger` field as-is)

  4b. Remove the method implementation. Current text (observed at lines 154-202, immediately after
  `GetEntriesByProductAsync` and immediately before the `private static IQueryable<JournalEntry> ApplySort`
  method):

  ```csharp
          public async Task<Dictionary<string, JournalIndicatorSnapshot>> GetJournalIndicatorsAsync(
              IEnumerable<string> productCodes,
              CancellationToken cancellationToken = default)
          {
              ArgumentNullException.ThrowIfNull(productCodes);
              var productCodeList = productCodes.ToList();

              // Aggregate direct associations into a per-product accumulator.
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

              var aggregatesByProduct = directAssociations.ToDictionary(x => x.ProductCode);

              var thirtyDaysAgo = DateTime.Today.AddDays(-RecentEntriesDays);
              var result = new Dictionary<string, JournalIndicatorSnapshot>(productCodeList.Count);

              foreach (var productCode in productCodeList)
              {
                  if (aggregatesByProduct.TryGetValue(productCode, out var aggregate))
                  {
                      var hasRecentEntries = aggregate.LastEntryDate >= thirtyDaysAgo;
                      result[productCode] = new JournalIndicatorSnapshot(
                          DirectEntries: aggregate.Count,
                          LastEntryDate: aggregate.LastEntryDate,
                          HasRecentEntries: hasRecentEntries);
                  }
                  else
                  {
                      result[productCode] = new JournalIndicatorSnapshot(
                          DirectEntries: 0,
                          LastEntryDate: null,
                          HasRecentEntries: false);
                  }
              }

              return result;
          }

  ```

  Delete this whole block, including its trailing blank line, so that `GetEntriesByProductAsync`'s
  closing brace is followed directly by the `private static IQueryable<JournalEntry> ApplySort(...)`
  method declaration:

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

          private static IQueryable<JournalEntry> ApplySort(
  ```

  Do not modify `GetByIdAsync`, `GetEntriesAsync`, `SearchEntriesAsync`, `GetEntriesByProductAsync`,
  `ApplySort`, `ApplyDefaultSort`, or `ApplyDefaultSortWithWarning` — only the constant and the one
  method body above are removed.

- [ ] **Step 5: Delete `JournalIndicatorDto.cs` entirely**

  ```bash
  rm backend/src/Anela.Heblo.Application/Features/Journal/Contracts/JournalIndicatorDto.cs
  ```

  This file's only content is the `JournalIndicatorDto` class, never referenced by any handler or
  controller.

- [ ] **Step 6: Remove the 4 dead `[Fact]` tests from `JournalRepositoryIntegrationTests.cs`**

  File: `backend/test/Anela.Heblo.Tests/Features/Journal/JournalRepositoryIntegrationTests.cs`

  There are two separate blocks to remove. Match on text, not line numbers, since Steps 2-5 do not
  touch this file but your own earlier edits in this same file (if you remove block A before block B)
  will shift block B's line numbers — always re-`grep -n "\[Fact\]"` after each removal within this
  file to find the next block's current position if you're unsure.

  **Block A — three consecutive tests** (`GetJournalIndicatorsAsync_WithMultipleDirectEntries_ReturnsCorrectCount`,
  `GetJournalIndicatorsAsync_WithNoEntries_ReturnsZeroIndicator`,
  `GetJournalIndicatorsAsync_WithRecentEntry_FlagsHasRecentEntries`), observed at lines 195-294,
  sitting between `GetEntriesByProductAsync_MultipleProducts_ShouldFindCorrectFamilyEntries` (ends with
  `}` on observed line 194) and the comment `// ---------- Sort matrix tests (FR-1 / FR-4) ----------`
  (observed line 296, with one blank line before it).

  Delete everything from the blank line immediately after `GetEntriesByProductAsync_MultipleProducts_ShouldFindCorrectFamilyEntries`'s
  closing brace through the closing brace of `GetJournalIndicatorsAsync_WithRecentEntry_FlagsHasRecentEntries`,
  i.e. this whole text block:

  ```csharp

      [Fact]
      public async Task GetJournalIndicatorsAsync_WithMultipleDirectEntries_ReturnsCorrectCount()
      {
          // Arrange
          var latest = DateTime.Today;
          var middle = DateTime.Today.AddDays(-1);
          var earliest = DateTime.Today.AddDays(-2);

          var e1 = new JournalEntry
          {
              Title = "TON002 entry 1",
              Content = "Content",
              EntryDate = latest,
              CreatedAt = DateTime.UtcNow,
              ModifiedAt = DateTime.UtcNow,
              CreatedByUserId = "test-user"
          };
          e1.AssociateWithProduct("TON002");

          var e2 = new JournalEntry
          {
              Title = "TON002 entry 2",
              Content = "Content",
              EntryDate = middle,
              CreatedAt = DateTime.UtcNow,
              ModifiedAt = DateTime.UtcNow,
              CreatedByUserId = "test-user"
          };
          e2.AssociateWithProduct("TON002");

          var e3 = new JournalEntry
          {
              Title = "TON002 entry 3",
              Content = "Content",
              EntryDate = earliest,
              CreatedAt = DateTime.UtcNow,
              ModifiedAt = DateTime.UtcNow,
              CreatedByUserId = "test-user"
          };
          e3.AssociateWithProduct("TON002");

          await _context.Set<JournalEntry>().AddRangeAsync(e1, e2, e3);
          await _context.SaveChangesAsync();

          // Act
          var result = await _repository.GetJournalIndicatorsAsync(new[] { "TON002" });

          // Assert
          result.Should().ContainKey("TON002");
          var indicator = result["TON002"];
          indicator.DirectEntries.Should().Be(3);
          indicator.LastEntryDate.Should().Be(latest);
          indicator.HasRecentEntries.Should().BeTrue();
      }

      [Fact]
      public async Task GetJournalIndicatorsAsync_WithNoEntries_ReturnsZeroIndicator()
      {
          // Arrange — intentionally no entries inserted

          // Act
          var result = await _repository.GetJournalIndicatorsAsync(new[] { "UNUSED999" });

          // Assert
          result.Should().ContainKey("UNUSED999");
          var indicator = result["UNUSED999"];
          indicator.DirectEntries.Should().Be(0);
          indicator.LastEntryDate.Should().BeNull();
          indicator.HasRecentEntries.Should().BeFalse();
      }

      [Fact]
      public async Task GetJournalIndicatorsAsync_WithRecentEntry_FlagsHasRecentEntries()
      {
          // Arrange
          var recent = DateTime.Today.AddDays(-5);
          var entry = new JournalEntry
          {
              Title = "Recent CREAM001 entry",
              Content = "Content",
              EntryDate = recent,
              CreatedAt = DateTime.UtcNow,
              ModifiedAt = DateTime.UtcNow,
              CreatedByUserId = "test-user"
          };
          entry.AssociateWithProduct("CREAM001");
          await _context.Set<JournalEntry>().AddAsync(entry);
          await _context.SaveChangesAsync();

          // Act
          var result = await _repository.GetJournalIndicatorsAsync(new[] { "CREAM001" });

          // Assert
          result.Should().ContainKey("CREAM001");
          var indicator = result["CREAM001"];
          indicator.DirectEntries.Should().Be(1);
          indicator.HasRecentEntries.Should().BeTrue();
          indicator.LastEntryDate.Should().Be(recent);
      }
  ```

  After deletion, exactly one blank line must remain between the prior test's closing `}` and the
  `// ---------- Sort matrix tests (FR-1 / FR-4) ----------` comment — do not leave a double blank
  line and do not collapse them onto the same line.

  **Block B — one test** (`GetJournalIndicatorsAsync_WhenEntryIsSoftDeleted_ExcludesFromCount`),
  observed at lines 767-798 (note: after Block A is deleted, this test's line numbers will have
  shifted up by ~100 lines — re-`grep -n "GetJournalIndicatorsAsync_WhenEntryIsSoftDeleted"` to find
  its new position before editing). It sits between
  `GetEntriesByProductAsync_WhenEntryIsSoftDeleted_ExcludesFromResults` (closing `}`) and the
  `private JournalEntry CreateEntryWithFamily(...)` helper method, with one blank line on each side.

  Delete this whole text block (the blank line before it through its closing brace):

  ```csharp

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

  After deletion, `GetEntriesByProductAsync_WhenEntryIsSoftDeleted_ExcludesFromResults`'s closing `}`
  must be followed by exactly one blank line and then `private JournalEntry CreateEntryWithFamily(...)`
  — same as before Block B existed.

  Do not touch any other test in the file (`GetEntriesByProductAsync_*`, the sort-matrix `[Theory]`
  tests, `GetEntriesAsync_*`, `SearchEntriesAsync_*`, `GetByIdAsync_WhenEntryIsSoftDeleted_ReturnsNull`,
  the `CreateEntryWithFamily`/`CreateEntryWithAuthor` helpers, `SeedSortFixtureAsync`, `SortMatrix()`,
  the constructor, or `Dispose()`).

- [ ] **Step 7: Confirm zero residual references**

  ```bash
  grep -rn "GetJournalIndicatorsAsync\|JournalIndicatorDto\|JournalIndicatorSnapshot" backend/
  ```

  Expected output: no matches (empty). If anything still matches, find and remove it before
  proceeding — it means a reference was missed.

- [ ] **Step 8: Build the solution**

  ```bash
  dotnet build Anela.Heblo.sln
  ```

  Expected: `Build succeeded.` with 0 errors. There should be no new warnings introduced by this
  change (a removed unused private field/method cannot introduce a new "unused member" warning,
  since the code no longer exists — if the build reports an unrelated pre-existing warning, that is
  fine and out of scope).

  If the build fails with a missing-member/type error, it means Step 7's grep missed a reference —
  go back and find/fix it; do not silence the error by re-adding any deleted code.

- [ ] **Step 9: Run the Journal test suite**

  ```bash
  dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~Features.Journal"
  ```

  Expected: all tests pass, and the total test count for this filter is 4 fewer than before this
  change (the 4 deleted `[Fact]` tests no longer exist to run). Confirm no test references
  `GetJournalIndicatorsAsync` fails to compile — if `dotnet build` in Step 8 succeeded, this is
  already guaranteed.

  If any previously-passing test in this file now fails, do not touch its assertions — instead
  check whether Step 6's edits accidentally altered a line inside a different test method (e.g. by
  deleting one line too many/few at a block boundary) and fix the accidental edit.

- [ ] **Step 10: Run the full solution test suite as a final safety net**

  ```bash
  dotnet test Anela.Heblo.sln
  ```

  Expected: all tests pass (no regressions outside the Journal module — this change touches no other
  module, so none are expected, but this is the final gate before committing).

- [ ] **Step 11: Commit**

  Stage exactly the files touched by this task (no `git add -A`):

  ```bash
  git add backend/src/Anela.Heblo.Domain/Features/Journal/IJournalRepository.cs \
          backend/src/Anela.Heblo.Persistence/Journal/JournalRepository.cs \
          backend/test/Anela.Heblo.Tests/Features/Journal/JournalRepositoryIntegrationTests.cs
  git rm backend/src/Anela.Heblo.Domain/Features/Journal/JournalIndicatorSnapshot.cs
  git rm backend/src/Anela.Heblo.Application/Features/Journal/Contracts/JournalIndicatorDto.cs
  git status
  ```

  Verify `git status` shows exactly these 5 files staged (2 deletions via `git rm`, 3 modifications)
  and nothing else. Then commit:

  ```bash
  git commit -m "$(cat <<'EOF'
  Remove dead GetJournalIndicatorsAsync code and JournalIndicatorDto

  IJournalRepository.GetJournalIndicatorsAsync, its JournalRepository
  implementation, the RecentEntriesDays constant, the JournalIndicatorSnapshot
  return type, and the never-wired JournalIndicatorDto had zero production
  callers (confirmed by repo-wide grep). Removing them along with the 4
  integration tests that existed solely to exercise the dead method.

  Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
  EOF
  )"
  ```

## Definition of done

- `IJournalRepository` declares 3 members, not 4.
- `JournalRepository.cs` has no `RecentEntriesDays` constant and no `GetJournalIndicatorsAsync` method.
- `JournalIndicatorSnapshot.cs` and `JournalIndicatorDto.cs` no longer exist.
- `JournalRepositoryIntegrationTests.cs` has no `GetJournalIndicatorsAsync_*` test methods; all other
  tests in the file are byte-for-byte unchanged.
- `grep -rn "GetJournalIndicatorsAsync\|JournalIndicatorDto\|JournalIndicatorSnapshot" backend/` is empty.
- `dotnet build Anela.Heblo.sln` succeeds.
- `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~Features.Journal"` passes with 4 fewer tests than before.
- `dotnet test Anela.Heblo.sln` passes in full.
- One commit exists containing exactly the 5 changed files.
