### task: fix-journal-entrydate-sort-warning

**Files:**
- Modify: `backend/src/Anela.Heblo.Persistence/Journal/JournalRepository.cs:152-163`
- Test: `backend/test/Anela.Heblo.Tests/Features/Journal/JournalRepositoryIntegrationTests.cs`

- [ ] **Step 1: Write the failing test**

  Open `backend/test/Anela.Heblo.Tests/Features/Journal/JournalRepositoryIntegrationTests.cs`. Add a new `[Fact]` test method immediately after the existing `GetEntriesAsync_WhitespaceSortBy_DoesNotLogWarning` method (i.e. right before the `SearchEntriesAsync_SortsByCreatedByUsername_Ascending` method, around line 320). This mirrors the existing "no warning logged" tests (`..._NullSortBy_DoesNotLogWarning`, `..._EmptySortBy_DoesNotLogWarning`, `..._WhitespaceSortBy_DoesNotLogWarning`) in its `Mock<ILogger<>>.Verify(..., Times.Never)` assertion style, and additionally asserts the resulting order is still by `EntryDate`, using the same three-distinct-dates seeding pattern already used by `SeedSortFixtureAsync`/`SortMatrix`.

  Insert this method:

  ```csharp
      [Fact]
      public async Task GetEntriesAsync_EntryDateSortBy_DoesNotLogWarning_AndSortsByEntryDate()
      {
          // Arrange — three entries with distinct EntryDate values.
          var alpha = CreateEntryWithAuthor("alice", new DateTime(2024, 1, 1), "Alpha");
          var bravo = CreateEntryWithAuthor("bob", new DateTime(2024, 2, 1), "Bravo");
          var charlie = CreateEntryWithAuthor("carol", new DateTime(2024, 3, 1), "Charlie");

          await _context.Set<JournalEntry>().AddRangeAsync(alpha, bravo, charlie);
          await _context.SaveChangesAsync();

          // Act — "EntryDate" is the default SortBy value sent by both request types.
          var result = await _repository.GetEntriesAsync(1, 10, sortBy: "EntryDate", sortDirection: "ASC");

          // Assert — still sorted by EntryDate ascending, exactly as before the fix.
          result.Items.Select(x => x.Title).Should().Equal("Alpha", "Bravo", "Charlie");

          // Assert — no "Unknown sort key" warning logged for the default sort key.
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

  Save the file. No other lines in this file change.

- [ ] **Step 2: Run the new test and confirm it fails**

  From the repo root, run:

  ```bash
  dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
    --filter "FullyQualifiedName~JournalRepositoryIntegrationTests.GetEntriesAsync_EntryDateSortBy_DoesNotLogWarning_AndSortsByEntryDate"
  ```

  Expected output: **1 test run, 1 failed.** The failure is on the `_loggerMock.Verify(..., Times.Never)` assertion (Moq throws `MockException: ... Expected invocation on the mock at most 1 times, but was 1 time` or similar "Times.Never" failure message), because today `sortBy = "EntryDate"` falls through the switch's `_` arm into `ApplyDefaultSortWithWarning`, which calls `logger.LogWarning(...)` once. The ordering assertion (`Should().Equal(...)`) passes on its own since `ApplyDefaultSortWithWarning` still delegates to `ApplyDefaultSort` — only the warning assertion fails. This confirms the test correctly detects the missing `"entrydate"` switch arm.

- [ ] **Step 3: Implement the minimal fix**

  Open `backend/src/Anela.Heblo.Persistence/Journal/JournalRepository.cs`. Locate the `ApplySort` switch expression (currently lines 152–163):

  ```csharp
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
  ```

  Replace it with (adding the new `"entrydate"` arm before `"title"`, matching the spec's proposed diff exactly):

  ```csharp
              return sortBy.ToLowerInvariant() switch
              {
                  "entrydate" => ApplyDefaultSort(query, ascending),

                  "title" => ascending
                      ? query.OrderBy(x => x.Title)
                      : query.OrderByDescending(x => x.Title),

                  "createdbyusername" => ascending
                      ? query.OrderBy(x => x.CreatedByUsername).ThenByDescending(x => x.EntryDate)
                      : query.OrderByDescending(x => x.CreatedByUsername).ThenByDescending(x => x.EntryDate),

                  _ => ApplyDefaultSortWithWarning(query, ascending, sortBy, logger),
              };
  ```

  Save the file. This is the only production-code change. `ApplyDefaultSort(query, ascending)` (lines 166–173) and `ApplyDefaultSortWithWarning(...)` (lines 175–187) are unchanged and reused as-is.

- [ ] **Step 4: Run the new test and confirm it passes**

  ```bash
  dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
    --filter "FullyQualifiedName~JournalRepositoryIntegrationTests.GetEntriesAsync_EntryDateSortBy_DoesNotLogWarning_AndSortsByEntryDate"
  ```

  Expected output: **1 test run, 1 passed.**

- [ ] **Step 5: Run the full Journal test file to confirm no regressions**

  ```bash
  dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
    --filter "FullyQualifiedName~Anela.Heblo.Tests.Features.Journal.JournalRepositoryIntegrationTests"
  ```

  Expected output: all tests in the file pass, including (at minimum) `GetEntriesAsync_UnknownSortBy_LogsWarningWithStructuredProperty` (still logs a warning for a genuinely unknown key like `"tags"`), `GetEntriesAsync_NullSortBy_DoesNotLogWarning`, `GetEntriesAsync_EmptySortBy_DoesNotLogWarning`, `GetEntriesAsync_WhitespaceSortBy_DoesNotLogWarning`, `GetEntriesAsync_SortByCreatedByUsername_TiebreaksByEntryDateDesc`, the `SortMatrix`-driven `GetEntriesAsync_AppliesExpectedOrdering` / `SearchEntriesAsync_AppliesExpectedOrdering` theories (which include `"unknown"` and `null` sortBy cases exercising the same `_`/default path), and the new `GetEntriesAsync_EntryDateSortBy_DoesNotLogWarning_AndSortsByEntryDate` test. No test count should decrease and none should fail.

- [ ] **Step 6: Build and format check**

  From the repo root:

  ```bash
  dotnet build Anela.Heblo.sln
  dotnet format Anela.Heblo.sln --verify-no-changes
  ```

  Expected output: build succeeds with 0 errors; `dotnet format --verify-no-changes` reports no formatting violations (exit code 0). If `dotnet format` reports violations, run `dotnet format Anela.Heblo.sln` (without `--verify-no-changes`) to apply fixes, then re-run Step 5 to confirm tests still pass before committing.

- [ ] **Step 7: Commit**

  ```bash
  git add backend/src/Anela.Heblo.Persistence/Journal/JournalRepository.cs \
          backend/test/Anela.Heblo.Tests/Features/Journal/JournalRepositoryIntegrationTests.cs
  git commit -m "$(cat <<'EOF'
  Fix: stop logging spurious "Unknown sort key" warning for default Journal sort

  JournalRepository.ApplySort() had no explicit switch arm for "entrydate",
  the lowercased form of the default SortBy value used by both
  GetJournalEntriesRequest and SearchJournalEntriesRequest. Every default
  page load fell through to ApplyDefaultSortWithWarning and logged a
  warning, even though the resulting sort was correct. Adds an explicit
  "entrydate" arm delegating to ApplyDefaultSort (no warning), following
  the same pattern already used for "createdbyusername" (#2502).

  Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
  Claude-Session: https://claude.ai/code/session_01EZBHepRSi772WQTTg8wr4b
  EOF
  )"
  ```

  Expected output: `git commit` succeeds and reports the new commit hash on the current branch; `git status` afterward shows a clean working tree for these two files.

---

## Self-Review

- **FR-1 (add explicit `"entrydate"` arm delegating to `ApplyDefaultSort`, no warning):** covered by Step 3, code shown in full, matches the spec's proposed diff verbatim.
- **AC "sortBy = EntryDate produces same IQueryable ordering as before":** covered by the new test's `result.Items.Select(x => x.Title).Should().Equal("Alpha", "Bravo", "Charlie")` assertion in Step 1/4.
- **AC "sortBy = EntryDate does not call ILogger.LogWarning":** covered by the new test's `_loggerMock.Verify(..., Times.Never)` assertion in Step 1/4.
- **AC "title / createdByUsername unaffected":** covered by Step 5 re-running the full existing test file, which includes `GetEntriesAsync_SortsByCreatedByUsername_Ascending`, `_Descending`, `_AcceptsAnyCasing`, `_TiebreaksByEntryDateDesc`, and the `SortMatrix` theories for `"title"`.
- **AC "unknown sortBy still logs warning":** covered by Step 5 re-running `GetEntriesAsync_UnknownSortBy_LogsWarningWithStructuredProperty`, unmodified, which must still pass because `"tags"` still falls to the `_` arm.
- **AC "null/empty/whitespace sortBy unaffected":** covered by Step 5 re-running `GetEntriesAsync_NullSortBy_DoesNotLogWarning`, `_EmptySortBy_DoesNotLogWarning`, `_WhitespaceSortBy_DoesNotLogWarning`, unmodified — these short-circuit before the switch (line 147–150, untouched by Step 3's edit).
- No placeholders: every step shows complete, real code and exact commands with expected output.
- Types/methods referenced (`JournalRepository`, `ApplySort`, `ApplyDefaultSort`, `ApplyDefaultSortWithWarning`, `JournalEntry`, `CreateEntryWithAuthor`, `_loggerMock`, `_repository`, `_context`) all already exist in the two files this task touches — no undefined references introduced.
- Out-of-scope items from the spec (broader `ApplySort` refactor, `SortBy` default value changes, new sort keys, auditing `IssuedInvoiceRepository`) are correctly not addressed by this task.
