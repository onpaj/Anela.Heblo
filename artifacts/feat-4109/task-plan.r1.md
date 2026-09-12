# JournalEntry.Create() Factory Method Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a `JournalEntry.Create(...)` static factory method that owns construction-time normalization (trim `Title`/`Content`, `.Date`-normalize `EntryDate`), and switch `CreateJournalEntryHandler` to call it instead of building the entity with an inline object initializer.

**Architecture:** Single new domain method placed alongside `Update()`/`SoftDelete()` in `JournalEntry.cs`, following the existing `static Create(...)` convention already used by `MarginLevel` and `InvoiceDqtResult` elsewhere in the domain layer. One call-site change in `CreateJournalEntryHandler.Handle()`. No schema, contract, or DI changes.

**Tech Stack:** .NET 8, C#, xUnit, FluentAssertions, Moq (existing test stack — no new packages)

---

### task: add-journalentry-create-factory

**Files:**
- Modify: `backend/src/Anela.Heblo.Domain/Features/Journal/JournalEntry.cs:151-153` (insert `Create()` before `Update()`)
- Modify: `backend/test/Anela.Heblo.Tests/Features/Journal/JournalEntryTests.cs` (add tests before the `// ----- Update -----` section, around line 219)

#### Background

`JournalEntry.cs` currently has this structure around the insertion point:

```csharp
        public void AssignTag(int tagId)
        {
            ...
        }

        public void ReplaceTagAssignments(IEnumerable<int>? tagIds)
        {
            ...
        }

        public void Update(string title, string content, DateTime entryDate, string userId, string username)
        {
            Title = title.Trim();
            Content = content.Trim();
            EntryDate = entryDate.Date;
            ModifiedAt = DateTime.UtcNow;
            ModifiedByUserId = userId;
            ModifiedByUsername = username;
        }
```

`JournalEntryTests.cs` uses a shared `NewEntry()` helper and FluentAssertions; the `// ----- Update -----` section (starting at line 219 in the current file) is the pattern to mirror.

- [ ] **Step 1: Write the failing tests for `JournalEntry.Create()`**

In `backend/test/Anela.Heblo.Tests/Features/Journal/JournalEntryTests.cs`, insert this new section immediately before the `// ----- Update -----` comment (before line 219):

```csharp
    // ----- Create -----

    [Fact]
    public void Create_TrimsTitleAndContentAndNormalizesEntryDate()
    {
        var now = new DateTime(2026, 6, 4, 9, 0, 0, DateTimeKind.Utc);

        var entry = JournalEntry.Create(
            title: "  My Title  ",
            content: "  Body text  ",
            entryDate: new DateTime(2026, 6, 4, 14, 30, 45, DateTimeKind.Utc),
            userId: "user-1",
            username: "Alice",
            now: now);

        entry.Title.Should().Be("My Title");
        entry.Content.Should().Be("Body text");
        entry.EntryDate.Should().Be(new DateTime(2026, 6, 4));
        entry.EntryDate.TimeOfDay.Should().Be(TimeSpan.Zero);
    }

    [Fact]
    public void Create_StampsCreatedAndModifiedAuditFieldsFromSuppliedNow()
    {
        var now = new DateTime(2026, 6, 4, 9, 0, 0, DateTimeKind.Utc);

        var entry = JournalEntry.Create(
            title: "t",
            content: "c",
            entryDate: DateTime.UtcNow,
            userId: "user-42",
            username: "Alice",
            now: now);

        entry.CreatedAt.Should().Be(now);
        entry.ModifiedAt.Should().Be(now);
        entry.CreatedAt.Should().Be(entry.ModifiedAt);
        entry.CreatedByUserId.Should().Be("user-42");
        entry.CreatedByUsername.Should().Be("Alice");
    }

    [Fact]
    public void Create_LeavesModificationAndDeletionAuditFieldsNull()
    {
        var entry = JournalEntry.Create(
            title: "t",
            content: "c",
            entryDate: DateTime.UtcNow,
            userId: "u",
            username: "n",
            now: DateTime.UtcNow);

        entry.ModifiedByUserId.Should().BeNull();
        entry.ModifiedByUsername.Should().BeNull();
        entry.IsDeleted.Should().BeFalse();
        entry.DeletedAt.Should().BeNull();
        entry.DeletedByUserId.Should().BeNull();
        entry.DeletedByUsername.Should().BeNull();
    }

    [Fact]
    public void Create_ReturnsEntryWithEmptyProductAndTagCollections()
    {
        var entry = JournalEntry.Create(
            title: "t",
            content: "c",
            entryDate: DateTime.UtcNow,
            userId: "u",
            username: "n",
            now: DateTime.UtcNow);

        entry.ProductAssociations.Should().NotBeNull().And.BeEmpty();
        entry.TagAssignments.Should().NotBeNull().And.BeEmpty();
    }

```

- [ ] **Step 2: Run the new tests to verify they fail**

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~JournalEntryTests.Create_" \
  --verbosity normal
```

Expected: build error `CS0117: 'JournalEntry' does not contain a definition for 'Create'` (the method does not exist yet).

- [ ] **Step 3: Implement `JournalEntry.Create()`**

In `backend/src/Anela.Heblo.Domain/Features/Journal/JournalEntry.cs`, insert this method directly above `public void Update(...)` (currently line 153):

```csharp
        public static JournalEntry Create(
            string title, string content, DateTime entryDate,
            string userId, string username, DateTime now)
        {
            var entry = new JournalEntry
            {
                CreatedAt = now,
                ModifiedAt = now,
                CreatedByUserId = userId,
                CreatedByUsername = username
            };

            entry.Title = title.Trim();
            entry.Content = content.Trim();
            entry.EntryDate = entryDate.Date;

            return entry;
        }

```

- [ ] **Step 4: Run the new tests to verify they pass**

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~JournalEntryTests.Create_" \
  --verbosity normal
```

Expected:
```
Passed!  - Failed: 0, Passed: 4, Skipped: 0
```

- [ ] **Step 5: Run the full `JournalEntryTests` file to check for regressions**

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~Anela.Heblo.Tests.Features.Journal.JournalEntryTests" \
  --verbosity minimal
```

Expected: all pre-existing tests in this file continue to pass alongside the 4 new ones.

- [ ] **Step 6: Commit**

```bash
git add backend/src/Anela.Heblo.Domain/Features/Journal/JournalEntry.cs \
        backend/test/Anela.Heblo.Tests/Features/Journal/JournalEntryTests.cs
git commit -m "feat(journal): add JournalEntry.Create() factory for construction-time normalization"
```

---

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
