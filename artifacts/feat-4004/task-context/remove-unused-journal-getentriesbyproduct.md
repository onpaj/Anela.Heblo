### task: remove-unused-journal-getentriesbyproduct

**Files:**
- `backend/src/Anela.Heblo.Domain/Features/Journal/IJournalRepository.cs` (edit — remove interface member)
- `backend/src/Anela.Heblo.Persistence/Journal/JournalRepository.cs` (edit — remove implementation)
- `backend/test/Anela.Heblo.Tests/Features/Journal/JournalRepositoryIntegrationTests.cs` (edit — remove 6 tests + conditionally the `CreateEntryWithFamily` helper)

**Steps:**

- [ ] **Step 1 — Confirm the baseline: current tests pass before touching anything.**

  Run just this test class:

  ```bash
  cd /home/user/worktrees/feature-4004-Arch-Review-Journal-Ijournalrepository-Getentriesb
  dotnet test Anela.Heblo.sln --filter "FullyQualifiedName~JournalRepositoryIntegrationTests"
  ```

  Expected output: `Passed! - Failed: 0, Passed: 18, Skipped: 0` (18 test methods: 9 `[Fact]`/`[Theory]`-driven cases visible directly plus the two `[Theory]` methods each run multiple times under `SortMatrix`/inline data — the important number is `Failed: 0`). If anything fails here, stop and investigate before proceeding — this task must not be used to paper over a pre-existing failure.

- [ ] **Step 2 — Remove `GetEntriesByProductAsync` from `IJournalRepository` (FR-1).**

  In `backend/src/Anela.Heblo.Domain/Features/Journal/IJournalRepository.cs`, find:

  ```csharp
              CancellationToken cancellationToken = default);

          Task<List<JournalEntry>> GetEntriesByProductAsync(
              string productCode,
              CancellationToken cancellationToken = default);
      }
  }
  ```

  Replace with:

  ```csharp
              CancellationToken cancellationToken = default);
      }
  }
  ```

  This deletes the `GetEntriesByProductAsync` signature (and the blank line that separated it from `SearchEntriesAsync`) while leaving `GetEntriesAsync` and `SearchEntriesAsync` byte-for-byte unchanged. The file should now contain exactly two members: `GetEntriesAsync` and `SearchEntriesAsync`.

- [ ] **Step 3 — Remove the `GetEntriesByProductAsync` implementation from `JournalRepository` (FR-2).**

  In `backend/src/Anela.Heblo.Persistence/Journal/JournalRepository.cs`, find:

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

  Replace with:

  ```csharp
          private static IQueryable<JournalEntry> ApplySort(
  ```

  Do not touch `GetEntriesAsync`, `SearchEntriesAsync`, `ApplySort`, `ApplyDefaultSort`, or `ApplyDefaultSortWithWarning` — they are unchanged.

  Note: at this point the solution will **not** compile, because the test file (edited next) still calls `_repository.GetEntriesByProductAsync(...)`. That is expected — do not run a build yet. Proceed directly to Step 4.

- [ ] **Step 4 — Remove the five `GetEntriesByProductAsync_*` tests that sit before the sort-matrix section (FR-3, part 1 of 2).**

  In `backend/test/Anela.Heblo.Tests/Features/Journal/JournalRepositoryIntegrationTests.cs`, find the block starting right after the constructor's closing brace and ending right before the `// ---------- Sort matrix tests (FR-1 / FR-4) ----------` comment:

  ```csharp
      [Fact]
      public async Task GetEntriesByProductAsync_WithProductCodePrefix_ShouldFindMatchingEntries()
      {
          // Arrange
          // Create journal entry associated with product family "TON002"
          var entry = new JournalEntry
          {
              Title = "Note about TON002 product family",
              Content = "This applies to all TON002 products including TON002030",
              EntryDate = DateTime.Now,
              CreatedAt = DateTime.UtcNow,
              ModifiedAt = DateTime.UtcNow,
              CreatedByUserId = "test-user"
          };
          entry.AssociateWithProduct("TON002");

          await _context.Set<JournalEntry>().AddAsync(entry);
          await _context.SaveChangesAsync();

          // Act - Test using GetEntriesByProductAsync which should find family entries
          var result = await _repository.GetEntriesByProductAsync("TON002030");

          // Assert
          result.Should().NotBeNull();
          result.Should().HaveCount(1, "TON002030 starts with TON002, so should find the family entry");
          result.First().Title.Should().Be("Note about TON002 product family");
          result.First().ProductAssociations.Should().HaveCount(1);
          result.First().ProductAssociations.First().ProductCodePrefix.Should().Be("TON002");
      }

      [Fact]
      public async Task GetEntriesByProductAsync_WithProductCode_ShouldFindFamilyEntries()
      {
          // Arrange
          // Create journal entry associated with product family "TON002"
          var familyEntry = new JournalEntry
          {
              Title = "Family note for TON002",
              Content = "Applies to all TON002 products",
              EntryDate = DateTime.Now,
              CreatedAt = DateTime.UtcNow,
              ModifiedAt = DateTime.UtcNow,
              CreatedByUserId = "test-user"
          };
          familyEntry.AssociateWithProduct("TON002");

          // Create journal entry for specific product
          var specificEntry = new JournalEntry
          {
              Title = "Specific note for TON002030",
              Content = "Only for TON002030",
              EntryDate = DateTime.Now.AddDays(-1),
              CreatedAt = DateTime.UtcNow.AddDays(-1),
              ModifiedAt = DateTime.UtcNow.AddDays(-1),
              CreatedByUserId = "test-user"
          };
          specificEntry.AssociateWithProduct("TON002030");

          // Create unrelated entry
          var unrelatedEntry = new JournalEntry
          {
              Title = "Unrelated note",
              Content = "For different product",
              EntryDate = DateTime.Now.AddDays(-2),
              CreatedAt = DateTime.UtcNow.AddDays(-2),
              ModifiedAt = DateTime.UtcNow.AddDays(-2),
              CreatedByUserId = "test-user"
          };
          unrelatedEntry.AssociateWithProduct("CREAM001");

          await _context.Set<JournalEntry>().AddRangeAsync(familyEntry, specificEntry, unrelatedEntry);
          await _context.SaveChangesAsync();

          // Act - search for product "TON002030"
          var result = await _repository.GetEntriesByProductAsync("TON002030");

          // Assert
          result.Should().NotBeNull();
          result.Should().HaveCount(2); // Should find both specific and family entries
          result.Should().Contain(e => e.Title == "Specific note for TON002030");
          result.Should().Contain(e => e.Title == "Family note for TON002");
          result.Should().NotContain(e => e.Title == "Unrelated note");
      }

      [Fact]
      public async Task GetEntriesByProductAsync_ProductStartsWithPrefix_ShouldMatchFamilyEntry()
      {
          // This is the critical test for the issue:
          // Product "TON002030" should find entries with prefix "TON002"

          // Arrange
          var entry = new JournalEntry
          {
              Title = "TON002 family documentation",
              Content = "Documentation for all TON002 products",
              EntryDate = DateTime.Now,
              CreatedAt = DateTime.UtcNow,
              ModifiedAt = DateTime.UtcNow,
              CreatedByUserId = "test-user"
          };
          entry.AssociateWithProduct("TON002");

          await _context.Set<JournalEntry>().AddAsync(entry);
          await _context.SaveChangesAsync();

          // Act - search for specific product that starts with the family prefix
          var result = await _repository.GetEntriesByProductAsync("TON002030");

          // Assert
          result.Should().NotBeNull();
          result.Should().HaveCount(1, "TON002030 starts with TON002, so it should match the family entry");
          result.First().Title.Should().Be("TON002 family documentation");
      }

      [Fact]
      public async Task GetEntriesByProductAsync_DifferentPrefix_ShouldNotMatch()
      {
          // Arrange
          var entry = new JournalEntry
          {
              Title = "CREAM family documentation",
              Content = "Documentation for CREAM products",
              EntryDate = DateTime.Now,
              CreatedAt = DateTime.UtcNow,
              ModifiedAt = DateTime.UtcNow,
              CreatedByUserId = "test-user"
          };
          entry.AssociateWithProduct("CREAM");

          await _context.Set<JournalEntry>().AddAsync(entry);
          await _context.SaveChangesAsync();

          // Act - search for product that doesn't start with CREAM
          var result = await _repository.GetEntriesByProductAsync("TON002030");

          // Assert
          result.Should().NotBeNull();
          result.Should().BeEmpty("TON002030 doesn't start with CREAM");
      }

      [Fact]
      public async Task GetEntriesByProductAsync_MultipleProducts_ShouldFindCorrectFamilyEntries()
      {
          // Arrange
          var entry1 = CreateEntryWithFamily("TON001", "TON001 family note");
          var entry2 = CreateEntryWithFamily("TON002", "TON002 family note");
          var entry3 = CreateEntryWithFamily("CREAM", "CREAM family note");

          await _context.Set<JournalEntry>().AddRangeAsync(entry1, entry2, entry3);
          await _context.SaveChangesAsync();

          // Act - Test both products should find their respective family entries
          var result1 = await _repository.GetEntriesByProductAsync("TON001030");
          var result2 = await _repository.GetEntriesByProductAsync("TON002030");
          var result3 = await _repository.GetEntriesByProductAsync("CREAM001");

          // Assert
          result1.Should().HaveCount(1);
          result1.First().Title.Should().Be("TON001 family note");

          result2.Should().HaveCount(1);
          result2.First().Title.Should().Be("TON002 family note");

          result3.Should().HaveCount(1);
          result3.First().Title.Should().Be("CREAM family note");
      }

      // ---------- Sort matrix tests (FR-1 / FR-4) ----------
  ```

  Replace with just:

  ```csharp
      // ---------- Sort matrix tests (FR-1 / FR-4) ----------
  ```

  This removes `GetEntriesByProductAsync_WithProductCodePrefix_ShouldFindMatchingEntries`, `GetEntriesByProductAsync_WithProductCode_ShouldFindFamilyEntries`, `GetEntriesByProductAsync_ProductStartsWithPrefix_ShouldMatchFamilyEntry`, `GetEntriesByProductAsync_DifferentPrefix_ShouldNotMatch`, and `GetEntriesByProductAsync_MultipleProducts_ShouldFindCorrectFamilyEntries`. Note it also removes the only three call sites of `CreateEntryWithFamily` (`entry1`/`entry2`/`entry3` above) — keep that in mind for Step 6.

- [ ] **Step 5 — Remove the sixth test, `GetEntriesByProductAsync_WhenEntryIsSoftDeleted_ExcludesFromResults` (FR-3, part 2 of 2).**

  In the same file, this test sits directly above the `CreateEntryWithFamily` helper, right after `SearchEntriesAsync_WhenEntryIsSoftDeleted_ExcludesFromResults`. Find:

  ```csharp
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

      private JournalEntry CreateEntryWithFamily(string prefix, string title)
      {
          var entry = new JournalEntry
          {
              Title = title,
              Content = $"Content for {prefix} family",
              EntryDate = DateTime.Now,
              CreatedAt = DateTime.UtcNow,
              ModifiedAt = DateTime.UtcNow,
              CreatedByUserId = "test-user"
          };
          entry.AssociateWithProduct(prefix);
          return entry;
      }

      private static JournalEntry CreateEntryWithAuthor(
  ```

  Replace with:

  ```csharp
      private static JournalEntry CreateEntryWithAuthor(
  ```

  This removes both the sixth test (`GetEntriesByProductAsync_WhenEntryIsSoftDeleted_ExcludesFromResults`) and the `CreateEntryWithFamily` helper in one edit, since — per Step 6 below — the helper has no remaining caller once Step 4 removes its only other usages.

- [ ] **Step 6 — Verify `CreateEntryWithFamily` is fully gone (confirms the conditional removal in Step 5 was correct).**

  ```bash
  cd /home/user/worktrees/feature-4004-Arch-Review-Journal-Ijournalrepository-Getentriesb
  grep -rn "CreateEntryWithFamily" backend/ frontend/
  ```

  Expected output: no matches (empty output, exit code 1). If this still shows a match, do not proceed — it means the helper has a caller you haven't accounted for; re-examine before continuing. (Per the spec/arch-review analysis, this is not expected to happen — the helper was used only by the test removed in Step 4.)

- [ ] **Step 7 — Build the backend and confirm it compiles clean.**

  ```bash
  cd /home/user/worktrees/feature-4004-Arch-Review-Journal-Ijournalrepository-Getentriesb
  dotnet build Anela.Heblo.sln
  ```

  Expected output: `Build succeeded.` with `0 Error(s)`. This is the concrete proof that:
  - `JournalRepository` still fully implements `IJournalRepository` (FR-2's acceptance criterion).
  - No other class in the codebase implements `IJournalRepository` (if one did, it would now fail to compile — this is FR-2's "verify via search" criterion made real by the compiler itself).
  - The test project no longer references the deleted method.

  If you see a compile error referencing `GetEntriesByProductAsync` anywhere outside the three files you edited, stop — that means an implementer or caller exists that the spec/arch-review did not find, and FR-2/FR-4 are not yet satisfied.

- [ ] **Step 8 — Run `dotnet format` (required by this repo's validation gate before any task is considered done).**

  ```bash
  cd /home/user/worktrees/feature-4004-Arch-Review-Journal-Ijournalrepository-Getentriesb
  dotnet format Anela.Heblo.sln --verify-no-changes
  ```

  If this reports formatting differences, run `dotnet format Anela.Heblo.sln` (without `--verify-no-changes`) to apply them, then re-run `--verify-no-changes` to confirm it's clean. Given the edits above are simple deletions that preserve existing indentation, no formatting changes are expected.

- [ ] **Step 9 — Run the full `JournalRepositoryIntegrationTests` class and confirm everything remaining passes.**

  ```bash
  cd /home/user/worktrees/feature-4004-Arch-Review-Journal-Ijournalrepository-Getentriesb
  dotnet test Anela.Heblo.sln --filter "FullyQualifiedName~JournalRepositoryIntegrationTests"
  ```

  Expected output: `Passed! - Failed: 0, Skipped: 0`, with the total passed count lower than Step 1's baseline (6 fewer `[Fact]` methods removed; the `[Theory]`-driven `GetEntriesAsync_AppliesExpectedOrdering` / `SearchEntriesAsync_AppliesExpectedOrdering` cases and the other 12 `[Fact]` tests — covering `GetEntriesAsync`, `SearchEntriesAsync`, the sort matrix, and soft-delete exclusion for both remaining methods — must all still be present and green). This confirms FR-3's acceptance criteria: no deleted test remains, and everything else still builds and passes.

- [ ] **Step 10 — Run the full backend test suite once, to catch any unexpected ripple.**

  ```bash
  cd /home/user/worktrees/feature-4004-Arch-Review-Journal-Ijournalrepository-Getentriesb
  dotnet test Anela.Heblo.sln
  ```

  Expected output: all tests pass (`Failed: 0`). No other test in the solution should reference `GetEntriesByProductAsync`, per Step 6's grep and Step 7's clean build, so this is a sanity check rather than an expected source of new failures.

- [ ] **Step 11 — Final repo-wide grep to confirm FR-4: no reference to `GetEntriesByProductAsync` remains anywhere.**

  ```bash
  cd /home/user/worktrees/feature-4004-Arch-Review-Journal-Ijournalrepository-Getentriesb
  grep -rn "GetEntriesByProductAsync" backend/ frontend/
  ```

  Expected output: no matches (empty output, exit code 1). This is FR-4's literal acceptance criterion.

- [ ] **Step 12 — Review the diff before committing.**

  ```bash
  cd /home/user/worktrees/feature-4004-Arch-Review-Journal-Ijournalrepository-Getentriesb
  git status
  git diff
  ```

  Confirm exactly three files are modified:
  - `backend/src/Anela.Heblo.Domain/Features/Journal/IJournalRepository.cs`
  - `backend/src/Anela.Heblo.Persistence/Journal/JournalRepository.cs`
  - `backend/test/Anela.Heblo.Tests/Features/Journal/JournalRepositoryIntegrationTests.cs`

  and that the diff is purely subtractive (no line outside the exact blocks shown in Steps 2, 3, 4, and 5 is touched).

- [ ] **Step 13 — Commit.**

  ```bash
  cd /home/user/worktrees/feature-4004-Arch-Review-Journal-Ijournalrepository-Getentriesb
  git add backend/src/Anela.Heblo.Domain/Features/Journal/IJournalRepository.cs \
          backend/src/Anela.Heblo.Persistence/Journal/JournalRepository.cs \
          backend/test/Anela.Heblo.Tests/Features/Journal/JournalRepositoryIntegrationTests.cs
  git commit -m "Remove unused IJournalRepository.GetEntriesByProductAsync

Dead code with zero production callers: SearchEntriesAsync's
productCodePrefix parameter already serves the same 'entries for a
product' capability and is what SearchJournalEntriesHandler and the
frontend useJournalEntriesByProduct hook actually use. Removes the
interface member, its EF Core implementation, the six dedicated
integration tests, and the now-unused CreateEntryWithFamily test
helper."
  ```

## Self-review checklist (for the engineer executing this plan)

- [ ] Every functional requirement in `spec.r1.md` maps to a step above: FR-1 → Step 2, FR-2 → Step 3, FR-3 → Steps 4–6, FR-4 → Step 11.
- [ ] No placeholder text ("TBD", "add appropriate...") appears anywhere in this plan — every code block above is the real, complete text to find or write.
- [ ] Method names, file paths, and class names are consistent across every step (`IJournalRepository`, `JournalRepository`, `JournalRepositoryIntegrationTests`, `CreateEntryWithFamily`).
- [ ] `GetEntriesAsync` and `SearchEntriesAsync` are never edited by this plan — verify the diff in Step 12 shows nothing touching those methods.
