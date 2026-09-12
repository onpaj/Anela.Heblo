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
