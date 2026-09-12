### task: use-create-factory-in-handler

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/Journal/UseCases/CreateJournalEntry/CreateJournalEntryHandler.cs:49-58`

**Depends on:** `add-journalentry-create-factory` (requires `JournalEntry.Create()` to exist).

#### Background

The current `Handle()` method builds the entity like this (lines 46–58):

```csharp
            var userId = currentUser.Id;
            var now = DateTime.UtcNow;

            var entry = new JournalEntry
            {
                Title = request.Title.Trim(),
                Content = request.Content.Trim(),
                EntryDate = request.EntryDate.Date,
                CreatedAt = now,
                ModifiedAt = now,
                CreatedByUserId = userId,
                CreatedByUsername = currentUser.Name ?? "Unknown User"
            };
```

Everything before and after this block (the authentication check, the blank-title check, the product-association loop, the tag-assignment loop, the repository calls, logging, and response construction) is unchanged by this task.

The existing test suite for this handler, `backend/test/Anela.Heblo.Tests/Features/Journal/CreateJournalEntryHandlerTests.cs`, already asserts observable behavior (trimmed title reaches the repository, `CreatedByUserId` is set, unauthorized/blank-title paths return the right error codes) — these tests are the regression guard for this task and require no code changes, only re-running.

- [ ] **Step 1: Confirm the existing handler tests pass before the change (baseline)**

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~CreateJournalEntryHandlerTests" \
  --verbosity normal
```

Expected: all existing tests pass (this establishes the pre-refactor baseline — no test changes are needed in this task since handler behavior does not change).

- [ ] **Step 2: Replace the object initializer with a call to `JournalEntry.Create()`**

In `backend/src/Anela.Heblo.Application/Features/Journal/UseCases/CreateJournalEntry/CreateJournalEntryHandler.cs`, replace:

```csharp
            var entry = new JournalEntry
            {
                Title = request.Title.Trim(),
                Content = request.Content.Trim(),
                EntryDate = request.EntryDate.Date,
                CreatedAt = now,
                ModifiedAt = now,
                CreatedByUserId = userId,
                CreatedByUsername = currentUser.Name ?? "Unknown User"
            };
```

with:

```csharp
            var entry = JournalEntry.Create(
                request.Title,
                request.Content,
                request.EntryDate,
                userId,
                currentUser.Name ?? "Unknown User",
                now);
```

- [ ] **Step 3: Build to verify it compiles**

```bash
dotnet build backend/src/Anela.Heblo.Application/Anela.Heblo.Application.csproj
```

Expected:
```
Build succeeded.
    0 Warning(s)
    0 Error(s)
```

- [ ] **Step 4: Run the handler tests again to confirm no regression**

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~CreateJournalEntryHandlerTests" \
  --verbosity normal
```

Expected: identical pass count and results to Step 1 — no test assertions should have needed to change. If `Handle_WhenValidRequest_ShouldCreateJournalEntrySuccessfully` or `Handle_WhenTitleIsWhitespaceOnly_TitleIsTrimmedBeforePersist` fail, check that the argument order passed to `JournalEntry.Create(...)` matches its signature exactly: `(title, content, entryDate, userId, username, now)`.

- [ ] **Step 5: Run the full Journal test suite to check for wider regressions**

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~Anela.Heblo.Tests.Features.Journal" \
  --verbosity minimal
```

Expected: all tests in `JournalEntryTests`, `JournalEntryMapperTests`, `GetJournalEntryHandlerTests`, `CreateJournalEntryHandlerTests`, `UpdateJournalEntryHandlerTests`, and `DeleteJournalEntryHandlerTests` pass.

- [ ] **Step 6: Run `dotnet format` to confirm no style drift**

```bash
dotnet format backend/Anela.Heblo.sln --verify-no-changes
```

Expected: no output / exit code 0. If it reports changes, run `dotnet format backend/Anela.Heblo.sln` and re-run Step 4 and Step 5.

- [ ] **Step 7: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Journal/UseCases/CreateJournalEntry/CreateJournalEntryHandler.cs
git commit -m "refactor(journal): construct JournalEntry via Create() factory instead of inline initializer"
```
