# Photobank ReapplyRules Handler Refactor Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Move the `ReapplyRules` tag-matching orchestration out of `PhotobankRepository` into `ReapplyRulesHandler`, reducing the repository to thin data-access primitives, with **identical resulting `PhotoTag` rows** for any input.

**Architecture:** The repository loses its 100-line `ReapplyRulesAsync` and gains five single-responsibility EF primitives (`GetAllPhotosAsync`, `RemoveRuleTagsAsync`, `GetOccupiedTagPairsAsync`, `GetOrCreateTagsAsync`, `AddPhotoTagsAsync`). The handler absorbs all branching, deduplication, source-precedence, and counting logic as pure in-memory work over materialized lists — making it unit-testable with a mocked `IPhotobankRepository`. `RetagPhotosHandler` is the model: sequence primitives + invalidate cache once.

**Tech Stack:** .NET 8, C#, EF Core (InMemory provider for tests), MediatR, xUnit, FluentAssertions, Moq.

---

## ⚠️ Critical behavior-preservation note (read before Task 6)

The architecture review's Decision-2 pseudocode hoists the empty-`ruleTagNames` early-return **before** the removal. **Do not do that — it breaks behavior.**

The current handler (`ReapplyRulesHandler.cs:36-38`) **always** calls `SaveChangesAsync` after `ReapplyRulesAsync` returns, even when the repository returns `0`. The repository stages the `RemoveRange` of rule tags (`PhotobankRepository.cs:285`) *before* its early `return 0` (`:308-309`). Therefore **today, the removal is committed even when there are no active rule tag names** (e.g. all rules inactive, or a scoped re-apply on an inactive rule). The spec's algorithm confirms this ordering: delete (step 2) precedes the empty-check (step 4).

To preserve behavior, the handler must:
1. `RemoveRuleTagsAsync(scope)` → `SaveChangesAsync()` **first, unconditionally** (this also avoids the EF change-tracker collision per Decision 2).
2. Compute `ruleTagNames`. If empty → `Invalidate()` and return `PhotosUpdated = 0` — **removals are already committed**.
3. Otherwise continue to the add phase.

This keeps Decision 2 (commit removals before re-adds) while correcting the early-return placement. The end-to-end test in Task 9 asserts removals **are** committed on the empty-rules path.

---

## File Structure

**Production (edit in place — no new production files):**
- `backend/src/Anela.Heblo.Domain/Features/Photobank/IPhotobankRepository.cs` — remove `ReapplyRulesAsync`; add five primitive signatures under their comment groups.
- `backend/src/Anela.Heblo.Application/Features/Photobank/PhotobankRepository.cs` — delete `ReapplyRulesAsync` (lines 273–372); add five primitive implementations.
- `backend/src/Anela.Heblo.Application/Features/Photobank/UseCases/ReapplyRules/ReapplyRulesHandler.cs` — absorb the orchestration. Request/Response unchanged.

**Tests:**
- `backend/test/Anela.Heblo.Tests/Features/Photobank/PhotobankRepositoryReapplyPrimitivesTests.cs` — **new** InMemory tests for each primitive.
- `backend/test/Anela.Heblo.Tests/Features/Photobank/ReapplyRulesHandlerTests.cs` — **rewrite** against mocked primitives.
- `backend/test/Anela.Heblo.Tests/Features/Photobank/ReapplyRulesBehaviorPreservationTests.cs` — **new** end-to-end tests against a real `ApplicationDbContext` (InMemory).
- `backend/test/Anela.Heblo.Tests/Features/Photobank/PhotobankTagsCacheInvalidationTests.cs` — fix the one `ReapplyRules` test (line 202–213) that mocks the removed method.

**Reference types (already exist — do not modify):**
- `Photo` (`Id`, `FolderPath`, `FileName`, `Tags`), `Tag` (`Id`, `Name`), `PhotoTag` (`PhotoId`, `TagId`, `Source`, `CreatedAt` — composite PK `(PhotoId, TagId)` shared across sources), `TagRule` (`Id`, `PathPattern`, `TagName`, `IsActive`, `SortOrder`), `PhotoTagSource` (`Rule = 0`, `Manual = 1`, `AI = 2`), `TagRuleMatcher.GetMatchingTags(folderPath, fileName, rules) → IReadOnlyList<string>` (returns lowercased names, filters `IsActive`, orders by `SortOrder`).
- DbSets: `_context.Photos`, `_context.PhotoTags`, `_context.PhotobankTags` (the `Tag` set).

---

## Task 1: `GetAllPhotosAsync` primitive

**Files:**
- Modify: `backend/src/Anela.Heblo.Domain/Features/Photobank/IPhotobankRepository.cs` (Photos group, after `GetLocatorAsync` at line 26)
- Modify: `backend/src/Anela.Heblo.Application/Features/Photobank/PhotobankRepository.cs` (Photos region)
- Test: `backend/test/Anela.Heblo.Tests/Features/Photobank/PhotobankRepositoryReapplyPrimitivesTests.cs` (new file)

- [ ] **Step 1: Write the failing test (creates the new test file)**

Create `backend/test/Anela.Heblo.Tests/Features/Photobank/PhotobankRepositoryReapplyPrimitivesTests.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Anela.Heblo.Application.Features.Photobank;
using Anela.Heblo.Domain.Features.Photobank;
using Anela.Heblo.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Anela.Heblo.Tests.Features.Photobank;

public class PhotobankRepositoryReapplyPrimitivesTests : IDisposable
{
    private readonly ApplicationDbContext _context;
    private readonly PhotobankRepository _repository;

    public PhotobankRepositoryReapplyPrimitivesTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        _context = new ApplicationDbContext(options);
        _repository = new PhotobankRepository(_context);
    }

    public void Dispose() => _context.Dispose();

    [Fact]
    public async System.Threading.Tasks.Task GetAllPhotosAsync_returnsAllPhotos()
    {
        // Arrange
        _context.Photos.AddRange(
            new Photo { Id = 1, SharePointFileId = "sp-1", FileName = "a.jpg", FolderPath = "Products", ModifiedAt = DateTime.UtcNow },
            new Photo { Id = 2, SharePointFileId = "sp-2", FileName = "b.jpg", FolderPath = "Events", ModifiedAt = DateTime.UtcNow });
        await _context.SaveChangesAsync(CancellationToken.None);

        // Act
        var photos = await _repository.GetAllPhotosAsync(CancellationToken.None);

        // Assert
        photos.Should().HaveCount(2);
        photos.Select(p => p.Id).Should().BeEquivalentTo(new[] { 1, 2 });
    }
}
```

- [ ] **Step 2: Run the test to verify it fails to compile**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~PhotobankRepositoryReapplyPrimitivesTests"`
Expected: BUILD FAILS — `'IPhotobankRepository' does not contain a definition for 'GetAllPhotosAsync'`.

- [ ] **Step 3: Add the interface signature**

In `IPhotobankRepository.cs`, in the `// Photos` group, immediately after the `GetLocatorAsync` line (line 26):

```csharp
        Task<List<Photo>> GetAllPhotosAsync(CancellationToken cancellationToken);
```

- [ ] **Step 4: Add the implementation**

In `PhotobankRepository.cs`, in the `// Photos` region (e.g. immediately after `GetLocatorAsync`, before the `// Tags` comment at line 139):

```csharp
        public async Task<List<Photo>> GetAllPhotosAsync(CancellationToken cancellationToken)
        {
            return await _context.Photos.ToListAsync(cancellationToken);
        }
```

- [ ] **Step 5: Run the test to verify it passes**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~GetAllPhotosAsync_returnsAllPhotos"`
Expected: PASS (1 passed).

- [ ] **Step 6: Commit**

```bash
git add backend/src/Anela.Heblo.Domain/Features/Photobank/IPhotobankRepository.cs \
        backend/src/Anela.Heblo.Application/Features/Photobank/PhotobankRepository.cs \
        backend/test/Anela.Heblo.Tests/Features/Photobank/PhotobankRepositoryReapplyPrimitivesTests.cs
git commit -m "feat: add GetAllPhotosAsync repository primitive for Photobank reapply"
```

---

## Task 2: `RemoveRuleTagsAsync` primitive (tracked `RemoveRange`, never saves)

**Files:**
- Modify: `IPhotobankRepository.cs` (Photo tags group, after `PhotoTagExistsAsync` at line 38)
- Modify: `PhotobankRepository.cs` (Photo tags region)
- Test: `PhotobankRepositoryReapplyPrimitivesTests.cs`

**Contract:** tracked `Where(pt => pt.Source == Rule)` plus `Where(pt => pt.Tag.Name == scopeToTagName)` when scoped → `RemoveRange`. **Does NOT call `SaveChangesAsync`** (the handler controls the save). Must use tracked `RemoveRange`, **not** `ExecuteDeleteAsync` — the InMemory provider does not support `ExecuteDeleteAsync` (Decision 1).

- [ ] **Step 1: Write the failing tests**

Append to `PhotobankRepositoryReapplyPrimitivesTests.cs` (inside the class):

```csharp
    [Fact]
    public async System.Threading.Tasks.Task RemoveRuleTagsAsync_unscoped_removesOnlyRuleTags()
    {
        // Arrange
        _context.Photos.Add(new Photo { Id = 1, SharePointFileId = "sp-1", FileName = "a.jpg", FolderPath = "P", ModifiedAt = DateTime.UtcNow });
        _context.PhotobankTags.AddRange(
            new Tag { Id = 10, Name = "products" },
            new Tag { Id = 11, Name = "manualtag" });
        _context.PhotoTags.AddRange(
            new PhotoTag { PhotoId = 1, TagId = 10, Source = PhotoTagSource.Rule, CreatedAt = DateTime.UtcNow },
            new PhotoTag { PhotoId = 1, TagId = 11, Source = PhotoTagSource.Manual, CreatedAt = DateTime.UtcNow });
        await _context.SaveChangesAsync(CancellationToken.None);

        // Act
        await _repository.RemoveRuleTagsAsync(null, CancellationToken.None);
        await _context.SaveChangesAsync(CancellationToken.None); // primitive does not save

        // Assert
        var remaining = await _context.PhotoTags.ToListAsync(CancellationToken.None);
        remaining.Should().ContainSingle();
        remaining[0].Source.Should().Be(PhotoTagSource.Manual);
    }

    [Fact]
    public async System.Threading.Tasks.Task RemoveRuleTagsAsync_scoped_removesOnlyMatchingTagName()
    {
        // Arrange
        _context.Photos.Add(new Photo { Id = 1, SharePointFileId = "sp-1", FileName = "a.jpg", FolderPath = "P", ModifiedAt = DateTime.UtcNow });
        _context.PhotobankTags.AddRange(
            new Tag { Id = 10, Name = "products" },
            new Tag { Id = 11, Name = "events" });
        _context.PhotoTags.AddRange(
            new PhotoTag { PhotoId = 1, TagId = 10, Source = PhotoTagSource.Rule, CreatedAt = DateTime.UtcNow },
            new PhotoTag { PhotoId = 1, TagId = 11, Source = PhotoTagSource.Rule, CreatedAt = DateTime.UtcNow });
        await _context.SaveChangesAsync(CancellationToken.None);

        // Act
        await _repository.RemoveRuleTagsAsync("products", CancellationToken.None);
        await _context.SaveChangesAsync(CancellationToken.None);

        // Assert
        var remaining = await _context.PhotoTags.ToListAsync(CancellationToken.None);
        remaining.Should().ContainSingle();
        remaining[0].TagId.Should().Be(11); // events untouched
    }

    [Fact]
    public async System.Threading.Tasks.Task RemoveRuleTagsAsync_doesNotSaveByItself()
    {
        // Arrange
        _context.Photos.Add(new Photo { Id = 1, SharePointFileId = "sp-1", FileName = "a.jpg", FolderPath = "P", ModifiedAt = DateTime.UtcNow });
        _context.PhotobankTags.Add(new Tag { Id = 10, Name = "products" });
        _context.PhotoTags.Add(new PhotoTag { PhotoId = 1, TagId = 10, Source = PhotoTagSource.Rule, CreatedAt = DateTime.UtcNow });
        await _context.SaveChangesAsync(CancellationToken.None);

        // Act — call the primitive but DO NOT save
        await _repository.RemoveRuleTagsAsync(null, CancellationToken.None);

        // Assert — the deletion is only staged; a fresh context still sees the row
        var verifyOptions = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: _context.Database.GetDbConnection is null ? Guid.NewGuid().ToString() : _context.ContextId.ToString())
            .Options;
        // Simpler: the change tracker holds it as Deleted but the store is unchanged until SaveChanges.
        _context.ChangeTracker.Entries<PhotoTag>()
            .Should().Contain(e => e.State == Microsoft.EntityFrameworkCore.EntityState.Deleted);
    }
```

> Note: the `RemoveRuleTagsAsync_doesNotSaveByItself` test asserts the change-tracker holds the entity as `Deleted` (not yet persisted). Keep only the `ChangeTracker.Entries` assertion; delete the unused `verifyOptions` lines if they complicate compilation — they are illustrative. The authoritative assertion is the `EntityState.Deleted` check.

- [ ] **Step 2: Run the tests to verify they fail to compile**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~RemoveRuleTagsAsync"`
Expected: BUILD FAILS — `'IPhotobankRepository' does not contain a definition for 'RemoveRuleTagsAsync'`.

- [ ] **Step 3: Add the interface signature**

In `IPhotobankRepository.cs`, in the `// Photo tags` group after `PhotoTagExistsAsync` (line 38):

```csharp
        Task RemoveRuleTagsAsync(string? scopeToTagName, CancellationToken cancellationToken);
```

- [ ] **Step 4: Add the implementation**

In `PhotobankRepository.cs`, in the `// Photo tags` region (after `PhotoTagExistsAsync`, before `// Roots` at line 213):

```csharp
        public async Task RemoveRuleTagsAsync(string? scopeToTagName, CancellationToken cancellationToken)
        {
            var query = _context.PhotoTags.Where(pt => pt.Source == PhotoTagSource.Rule);
            if (scopeToTagName != null)
                query = query.Where(pt => pt.Tag.Name == scopeToTagName);

            var ruleTags = await query.ToListAsync(cancellationToken);
            _context.PhotoTags.RemoveRange(ruleTags);
        }
```

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~RemoveRuleTagsAsync"`
Expected: PASS (3 passed).

- [ ] **Step 6: Commit**

```bash
git add backend/src/Anela.Heblo.Domain/Features/Photobank/IPhotobankRepository.cs \
        backend/src/Anela.Heblo.Application/Features/Photobank/PhotobankRepository.cs \
        backend/test/Anela.Heblo.Tests/Features/Photobank/PhotobankRepositoryReapplyPrimitivesTests.cs
git commit -m "feat: add RemoveRuleTagsAsync repository primitive (tracked RemoveRange, no save)"
```

---

## Task 3: `GetOccupiedTagPairsAsync` primitive (Manual/AI shared-PK snapshot)

**Files:**
- Modify: `IPhotobankRepository.cs` (Photo tags group)
- Modify: `PhotobankRepository.cs` (Photo tags region)
- Test: `PhotobankRepositoryReapplyPrimitivesTests.cs`

**Contract:** `Where(pt => pt.Source != Rule)` + scope filter, project `(PhotoId, TagId)`, return as `HashSet<(int PhotoId, int TagId)>`. Read-only (no save). This is the snapshot that enforces Manual/AI precedence: a Rule tag cannot be inserted where a Manual/AI tag already occupies the same composite-PK pair.

- [ ] **Step 1: Write the failing tests**

Append to `PhotobankRepositoryReapplyPrimitivesTests.cs`:

```csharp
    [Fact]
    public async System.Threading.Tasks.Task GetOccupiedTagPairsAsync_unscoped_returnsOnlyNonRulePairs()
    {
        // Arrange
        _context.Photos.Add(new Photo { Id = 1, SharePointFileId = "sp-1", FileName = "a.jpg", FolderPath = "P", ModifiedAt = DateTime.UtcNow });
        _context.PhotobankTags.AddRange(
            new Tag { Id = 10, Name = "products" },
            new Tag { Id = 11, Name = "aitag" },
            new Tag { Id = 12, Name = "ruletag" });
        _context.PhotoTags.AddRange(
            new PhotoTag { PhotoId = 1, TagId = 10, Source = PhotoTagSource.Manual, CreatedAt = DateTime.UtcNow },
            new PhotoTag { PhotoId = 1, TagId = 11, Source = PhotoTagSource.AI, CreatedAt = DateTime.UtcNow },
            new PhotoTag { PhotoId = 1, TagId = 12, Source = PhotoTagSource.Rule, CreatedAt = DateTime.UtcNow });
        await _context.SaveChangesAsync(CancellationToken.None);

        // Act
        var occupied = await _repository.GetOccupiedTagPairsAsync(null, CancellationToken.None);

        // Assert
        occupied.Should().BeEquivalentTo(new HashSet<(int, int)> { (1, 10), (1, 11) });
        occupied.Should().NotContain((1, 12)); // Rule pair excluded
    }

    [Fact]
    public async System.Threading.Tasks.Task GetOccupiedTagPairsAsync_scoped_filtersByTagName()
    {
        // Arrange
        _context.Photos.Add(new Photo { Id = 1, SharePointFileId = "sp-1", FileName = "a.jpg", FolderPath = "P", ModifiedAt = DateTime.UtcNow });
        _context.PhotobankTags.AddRange(
            new Tag { Id = 10, Name = "products" },
            new Tag { Id = 11, Name = "events" });
        _context.PhotoTags.AddRange(
            new PhotoTag { PhotoId = 1, TagId = 10, Source = PhotoTagSource.Manual, CreatedAt = DateTime.UtcNow },
            new PhotoTag { PhotoId = 1, TagId = 11, Source = PhotoTagSource.Manual, CreatedAt = DateTime.UtcNow });
        await _context.SaveChangesAsync(CancellationToken.None);

        // Act
        var occupied = await _repository.GetOccupiedTagPairsAsync("products", CancellationToken.None);

        // Assert
        occupied.Should().BeEquivalentTo(new HashSet<(int, int)> { (1, 10) });
    }
```

- [ ] **Step 2: Run the tests to verify they fail to compile**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~GetOccupiedTagPairsAsync"`
Expected: BUILD FAILS — method does not exist.

- [ ] **Step 3: Add the interface signature**

In `IPhotobankRepository.cs`, `// Photo tags` group, after the `RemoveRuleTagsAsync` line added in Task 2:

```csharp
        Task<HashSet<(int PhotoId, int TagId)>> GetOccupiedTagPairsAsync(string? scopeToTagName, CancellationToken cancellationToken);
```

- [ ] **Step 4: Add the implementation**

In `PhotobankRepository.cs`, `// Photo tags` region, after `RemoveRuleTagsAsync`:

```csharp
        public async Task<HashSet<(int PhotoId, int TagId)>> GetOccupiedTagPairsAsync(
            string? scopeToTagName, CancellationToken cancellationToken)
        {
            var query = _context.PhotoTags.Where(pt => pt.Source != PhotoTagSource.Rule);
            if (scopeToTagName != null)
                query = query.Where(pt => pt.Tag.Name == scopeToTagName);

            var pairs = await query
                .Select(pt => new { pt.PhotoId, pt.TagId })
                .ToListAsync(cancellationToken);

            return pairs.Select(x => (x.PhotoId, x.TagId)).ToHashSet();
        }
```

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~GetOccupiedTagPairsAsync"`
Expected: PASS (2 passed).

- [ ] **Step 6: Commit**

```bash
git add backend/src/Anela.Heblo.Domain/Features/Photobank/IPhotobankRepository.cs \
        backend/src/Anela.Heblo.Application/Features/Photobank/PhotobankRepository.cs \
        backend/test/Anela.Heblo.Tests/Features/Photobank/PhotobankRepositoryReapplyPrimitivesTests.cs
git commit -m "feat: add GetOccupiedTagPairsAsync repository primitive for Manual/AI precedence"
```

---

## Task 4: `AddPhotoTagsAsync` primitive (batch `AddRange`, never saves)

**Files:**
- Modify: `IPhotobankRepository.cs` (Photo tags group)
- Modify: `PhotobankRepository.cs` (Photo tags region)
- Test: `PhotobankRepositoryReapplyPrimitivesTests.cs`

**Contract:** `_context.PhotoTags.AddRange(...)`. **Does NOT save** (mirrors existing `AddPhotoTagAsync` at `:193-197`).

- [ ] **Step 1: Write the failing test**

Append to `PhotobankRepositoryReapplyPrimitivesTests.cs`:

```csharp
    [Fact]
    public async System.Threading.Tasks.Task AddPhotoTagsAsync_stagesRows_persistedAfterSave()
    {
        // Arrange
        _context.Photos.Add(new Photo { Id = 1, SharePointFileId = "sp-1", FileName = "a.jpg", FolderPath = "P", ModifiedAt = DateTime.UtcNow });
        _context.PhotobankTags.Add(new Tag { Id = 10, Name = "products" });
        await _context.SaveChangesAsync(CancellationToken.None);

        var toAdd = new List<PhotoTag>
        {
            new() { PhotoId = 1, TagId = 10, Source = PhotoTagSource.Rule, CreatedAt = DateTime.UtcNow },
        };

        // Act
        await _repository.AddPhotoTagsAsync(toAdd, CancellationToken.None);
        await _context.SaveChangesAsync(CancellationToken.None); // primitive does not save

        // Assert
        var rows = await _context.PhotoTags.ToListAsync(CancellationToken.None);
        rows.Should().ContainSingle();
        rows[0].Source.Should().Be(PhotoTagSource.Rule);
    }
```

- [ ] **Step 2: Run the test to verify it fails to compile**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~AddPhotoTagsAsync_stagesRows"`
Expected: BUILD FAILS — method does not exist.

- [ ] **Step 3: Add the interface signature**

In `IPhotobankRepository.cs`, `// Photo tags` group, after `GetOccupiedTagPairsAsync`:

```csharp
        Task AddPhotoTagsAsync(IEnumerable<PhotoTag> photoTags, CancellationToken cancellationToken);
```

- [ ] **Step 4: Add the implementation**

In `PhotobankRepository.cs`, `// Photo tags` region, after `GetOccupiedTagPairsAsync`:

```csharp
        public Task AddPhotoTagsAsync(IEnumerable<PhotoTag> photoTags, CancellationToken cancellationToken)
        {
            _context.PhotoTags.AddRange(photoTags);
            return Task.CompletedTask;
        }
```

- [ ] **Step 5: Run the test to verify it passes**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~AddPhotoTagsAsync_stagesRows"`
Expected: PASS (1 passed).

- [ ] **Step 6: Commit**

```bash
git add backend/src/Anela.Heblo.Domain/Features/Photobank/IPhotobankRepository.cs \
        backend/src/Anela.Heblo.Application/Features/Photobank/PhotobankRepository.cs \
        backend/test/Anela.Heblo.Tests/Features/Photobank/PhotobankRepositoryReapplyPrimitivesTests.cs
git commit -m "feat: add AddPhotoTagsAsync batch repository primitive (no save)"
```

---

## Task 5: `GetOrCreateTagsAsync` batch primitive (single load + single flush; returns name→Id map)

**Files:**
- Modify: `IPhotobankRepository.cs` (Tags group, after `GetOrCreateTagAsync` at line 30)
- Modify: `PhotobankRepository.cs` (Tags region)
- Test: `PhotobankRepositoryReapplyPrimitivesTests.cs`

**Contract (mirrors the resolve+create slice of the old `ReapplyRulesAsync:311-326`):** single query for existing tags by name; create missing as `new Tag { Name = name }`; **flush only when new tags were created** (matches `GetOrCreateTagAsync`'s save-internally precedent and the old code's `if (newTagsCreated)`); return `IReadOnlyDictionary<string, int>` (name→Id). Returning IDs (not tracked entities) keeps tracking concerns inside the repository. Single round-trip — **not** a loop over `GetOrCreateTagAsync` (avoids N+1, Decision 3).

- [ ] **Step 1: Write the failing tests**

Append to `PhotobankRepositoryReapplyPrimitivesTests.cs`:

```csharp
    [Fact]
    public async System.Threading.Tasks.Task GetOrCreateTagsAsync_returnsExistingIds_andCreatesMissing()
    {
        // Arrange
        _context.PhotobankTags.Add(new Tag { Id = 10, Name = "products" });
        await _context.SaveChangesAsync(CancellationToken.None);

        // Act — "products" exists, "events" is new
        var map = await _repository.GetOrCreateTagsAsync(new[] { "products", "events" }, CancellationToken.None);

        // Assert
        map.Should().ContainKey("products").WhoseValue.Should().Be(10);
        map.Should().ContainKey("events");
        map["events"].Should().BeGreaterThan(0); // DB-assigned id

        var persisted = await _context.PhotobankTags.ToListAsync(CancellationToken.None);
        persisted.Select(t => t.Name).Should().BeEquivalentTo(new[] { "products", "events" });
    }

    [Fact]
    public async System.Threading.Tasks.Task GetOrCreateTagsAsync_allExisting_returnsIdsWithoutCreating()
    {
        // Arrange
        _context.PhotobankTags.AddRange(
            new Tag { Id = 10, Name = "products" },
            new Tag { Id = 11, Name = "events" });
        await _context.SaveChangesAsync(CancellationToken.None);

        // Act
        var map = await _repository.GetOrCreateTagsAsync(new[] { "products", "events" }, CancellationToken.None);

        // Assert
        map["products"].Should().Be(10);
        map["events"].Should().Be(11);
        (await _context.PhotobankTags.CountAsync(CancellationToken.None)).Should().Be(2);
    }
```

- [ ] **Step 2: Run the tests to verify they fail to compile**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~GetOrCreateTagsAsync"`
Expected: BUILD FAILS — method does not exist.

- [ ] **Step 3: Add the interface signature**

In `IPhotobankRepository.cs`, `// Tags` group, immediately after `GetOrCreateTagAsync` (line 30):

```csharp
        Task<IReadOnlyDictionary<string, int>> GetOrCreateTagsAsync(IReadOnlyCollection<string> normalizedNames, CancellationToken cancellationToken);
```

- [ ] **Step 4: Add the implementation**

In `PhotobankRepository.cs`, `// Tags` region, after `GetOrCreateTagAsync` (line 171):

```csharp
        public async Task<IReadOnlyDictionary<string, int>> GetOrCreateTagsAsync(
            IReadOnlyCollection<string> normalizedNames, CancellationToken cancellationToken)
        {
            var tagsByName = await _context.PhotobankTags
                .Where(t => normalizedNames.Contains(t.Name))
                .ToDictionaryAsync(t => t.Name, cancellationToken);

            var newTagsCreated = false;
            foreach (var name in normalizedNames.Where(n => !tagsByName.ContainsKey(n)))
            {
                var newTag = new Tag { Name = name };
                _context.PhotobankTags.Add(newTag);
                tagsByName[name] = newTag;
                newTagsCreated = true;
            }

            // Flush new Tag inserts so they receive DB-assigned IDs before use.
            if (newTagsCreated)
                await _context.SaveChangesAsync(cancellationToken);

            return tagsByName.ToDictionary(kvp => kvp.Key, kvp => kvp.Value.Id);
        }
```

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~GetOrCreateTagsAsync"`
Expected: PASS (2 passed).

- [ ] **Step 6: Commit**

```bash
git add backend/src/Anela.Heblo.Domain/Features/Photobank/IPhotobankRepository.cs \
        backend/src/Anela.Heblo.Application/Features/Photobank/PhotobankRepository.cs \
        backend/test/Anela.Heblo.Tests/Features/Photobank/PhotobankRepositoryReapplyPrimitivesTests.cs
git commit -m "feat: add GetOrCreateTagsAsync batch repository primitive (name->Id map)"
```

---

## Task 6: Move orchestration into `ReapplyRulesHandler` + rewrite handler unit tests

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/Photobank/UseCases/ReapplyRules/ReapplyRulesHandler.cs`
- Rewrite: `backend/test/Anela.Heblo.Tests/Features/Photobank/ReapplyRulesHandlerTests.cs`

**Read the "Critical behavior-preservation note" at the top of this plan before starting.** Order of operations is binding: remove + commit happens **before** the empty-`ruleTagNames` early-return.

- [ ] **Step 1: Rewrite the handler unit tests (these are the failing tests)**

Replace the **entire** contents of `ReapplyRulesHandlerTests.cs` with:

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Anela.Heblo.Application.Features.Photobank.Services;
using Anela.Heblo.Application.Features.Photobank.UseCases.ReapplyRules;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Photobank;
using FluentAssertions;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Photobank;

public class ReapplyRulesHandlerTests
{
    private readonly Mock<IPhotobankRepository> _repo = new();
    private readonly Mock<IPhotobankTagsCache> _cache = new();
    private readonly ReapplyRulesHandler _handler;

    public ReapplyRulesHandlerTests()
    {
        _handler = new ReapplyRulesHandler(_repo.Object, _cache.Object);

        // Sensible defaults so unconfigured calls don't NRE.
        _repo.Setup(r => r.RemoveRuleTagsAsync(It.IsAny<string?>(), It.IsAny<CancellationToken>()))
             .Returns(Task.CompletedTask);
        _repo.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
             .Returns(Task.CompletedTask);
        _repo.Setup(r => r.GetOccupiedTagPairsAsync(It.IsAny<string?>(), It.IsAny<CancellationToken>()))
             .ReturnsAsync(new HashSet<(int PhotoId, int TagId)>());
        _repo.Setup(r => r.GetAllPhotosAsync(It.IsAny<CancellationToken>()))
             .ReturnsAsync(new List<Photo>());
        _repo.Setup(r => r.AddPhotoTagsAsync(It.IsAny<IEnumerable<PhotoTag>>(), It.IsAny<CancellationToken>()))
             .Returns(Task.CompletedTask);
        _repo.Setup(r => r.GetOrCreateTagsAsync(It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<CancellationToken>()))
             .ReturnsAsync(new Dictionary<string, int>());
    }

    private static Photo PhotoAt(int id, string folder, string file) =>
        new() { Id = id, SharePointFileId = $"sp-{id}", FolderPath = folder, FileName = file, ModifiedAt = DateTime.UtcNow };

    [Fact]
    public async Task RuleNotFound_ReturnsError_AndDoesNotRemoveOrSave()
    {
        _repo.Setup(r => r.GetRulesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new List<TagRule>());

        var result = await _handler.Handle(new ReapplyRulesRequest { RuleId = 99 }, CancellationToken.None);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.PhotobankRuleNotFound);
        _repo.Verify(r => r.RemoveRuleTagsAsync(It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
        _repo.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task NoActiveRuleTagNames_CommitsRemovalThenReturnsZero_AndInvalidates()
    {
        // All rules inactive → ruleTagNames empty. Removal must still be committed (behavior preservation).
        _repo.Setup(r => r.GetRulesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new List<TagRule>
        {
            new() { Id = 1, PathPattern = "P", TagName = "products", IsActive = false, SortOrder = 0 },
        });

        var result = await _handler.Handle(new ReapplyRulesRequest(), CancellationToken.None);

        result.Success.Should().BeTrue();
        result.PhotosUpdated.Should().Be(0);
        _repo.Verify(r => r.RemoveRuleTagsAsync(null, It.IsAny<CancellationToken>()), Times.Once);
        _repo.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once); // removal committed
        _repo.Verify(r => r.GetOrCreateTagsAsync(It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<CancellationToken>()), Times.Never);
        _repo.Verify(r => r.AddPhotoTagsAsync(It.IsAny<IEnumerable<PhotoTag>>(), It.IsAny<CancellationToken>()), Times.Never);
        _cache.Verify(c => c.Invalidate(), Times.Once);
    }

    [Fact]
    public async Task HappyPath_AddsRuleTags_CountsPhotos_InvalidatesOnce()
    {
        _repo.Setup(r => r.GetRulesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new List<TagRule>
        {
            new() { Id = 1, PathPattern = "Products", TagName = "products", IsActive = true, SortOrder = 0 },
        });
        _repo.Setup(r => r.GetOrCreateTagsAsync(It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<CancellationToken>()))
             .ReturnsAsync(new Dictionary<string, int> { ["products"] = 10 });
        _repo.Setup(r => r.GetAllPhotosAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new List<Photo>
        {
            PhotoAt(1, "Products/A", "a.jpg"),
            PhotoAt(2, "Products/B", "b.jpg"),
            PhotoAt(3, "Events/C", "c.jpg"), // no match
        });

        List<PhotoTag>? added = null;
        _repo.Setup(r => r.AddPhotoTagsAsync(It.IsAny<IEnumerable<PhotoTag>>(), It.IsAny<CancellationToken>()))
             .Callback<IEnumerable<PhotoTag>, CancellationToken>((tags, _) => added = tags.ToList())
             .Returns(Task.CompletedTask);

        var result = await _handler.Handle(new ReapplyRulesRequest(), CancellationToken.None);

        result.PhotosUpdated.Should().Be(2);
        added.Should().NotBeNull();
        added!.Should().HaveCount(2);
        added.Should().OnlyContain(t => t.Source == PhotoTagSource.Rule && t.TagId == 10);
        added.Select(t => t.PhotoId).Should().BeEquivalentTo(new[] { 1, 2 });
        _cache.Verify(c => c.Invalidate(), Times.Once);
    }

    [Fact]
    public async Task ManualAiPrecedence_OccupiedPairNotAdded()
    {
        _repo.Setup(r => r.GetRulesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new List<TagRule>
        {
            new() { Id = 1, PathPattern = "Products", TagName = "products", IsActive = true, SortOrder = 0 },
        });
        _repo.Setup(r => r.GetOrCreateTagsAsync(It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<CancellationToken>()))
             .ReturnsAsync(new Dictionary<string, int> { ["products"] = 10 });
        _repo.Setup(r => r.GetAllPhotosAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new List<Photo>
        {
            PhotoAt(1, "Products/A", "a.jpg"),
        });
        _repo.Setup(r => r.GetOccupiedTagPairsAsync(It.IsAny<string?>(), It.IsAny<CancellationToken>()))
             .ReturnsAsync(new HashSet<(int PhotoId, int TagId)> { (1, 10) }); // Manual/AI already owns it

        List<PhotoTag>? added = null;
        _repo.Setup(r => r.AddPhotoTagsAsync(It.IsAny<IEnumerable<PhotoTag>>(), It.IsAny<CancellationToken>()))
             .Callback<IEnumerable<PhotoTag>, CancellationToken>((tags, _) => added = tags.ToList())
             .Returns(Task.CompletedTask);

        var result = await _handler.Handle(new ReapplyRulesRequest(), CancellationToken.None);

        result.PhotosUpdated.Should().Be(0);
        added!.Should().BeEmpty();
    }

    [Fact]
    public async Task DuplicateMatch_CountedOnce()
    {
        // Two active rules produce the SAME tag name and both match the photo.
        _repo.Setup(r => r.GetRulesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new List<TagRule>
        {
            new() { Id = 1, PathPattern = "Products", TagName = "products", IsActive = true, SortOrder = 0 },
            new() { Id = 2, PathPattern = "A", TagName = "products", IsActive = true, SortOrder = 1 },
        });
        _repo.Setup(r => r.GetOrCreateTagsAsync(It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<CancellationToken>()))
             .ReturnsAsync(new Dictionary<string, int> { ["products"] = 10 });
        _repo.Setup(r => r.GetAllPhotosAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new List<Photo>
        {
            PhotoAt(1, "Products/A", "a.jpg"), // matches both rules → still one (1,10) pair
        });

        List<PhotoTag>? added = null;
        _repo.Setup(r => r.AddPhotoTagsAsync(It.IsAny<IEnumerable<PhotoTag>>(), It.IsAny<CancellationToken>()))
             .Callback<IEnumerable<PhotoTag>, CancellationToken>((tags, _) => added = tags.ToList())
             .Returns(Task.CompletedTask);

        var result = await _handler.Handle(new ReapplyRulesRequest(), CancellationToken.None);

        result.PhotosUpdated.Should().Be(1);
        added!.Should().ContainSingle();
    }

    [Fact]
    public async Task SingleRule_ScopesEveryStepToTagName()
    {
        _repo.Setup(r => r.GetRulesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new List<TagRule>
        {
            new() { Id = 1, PathPattern = "Products", TagName = "Products", IsActive = true, SortOrder = 0 },
            new() { Id = 2, PathPattern = "Events", TagName = "events", IsActive = true, SortOrder = 1 },
        });
        _repo.Setup(r => r.GetOrCreateTagsAsync(It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<CancellationToken>()))
             .ReturnsAsync(new Dictionary<string, int> { ["products"] = 10 });
        _repo.Setup(r => r.GetAllPhotosAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new List<Photo>
        {
            PhotoAt(1, "Products/Events", "a.jpg"), // matches both rules' patterns
        });

        IReadOnlyCollection<string>? requestedNames = null;
        _repo.Setup(r => r.GetOrCreateTagsAsync(It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<CancellationToken>()))
             .Callback<IReadOnlyCollection<string>, CancellationToken>((names, _) => requestedNames = names)
             .ReturnsAsync(new Dictionary<string, int> { ["products"] = 10 });

        List<PhotoTag>? added = null;
        _repo.Setup(r => r.AddPhotoTagsAsync(It.IsAny<IEnumerable<PhotoTag>>(), It.IsAny<CancellationToken>()))
             .Callback<IEnumerable<PhotoTag>, CancellationToken>((tags, _) => added = tags.ToList())
             .Returns(Task.CompletedTask);

        var result = await _handler.Handle(new ReapplyRulesRequest { RuleId = 1 }, CancellationToken.None);

        // scope = "products" (lowercased) threaded through removal, occupied snapshot, tag set
        _repo.Verify(r => r.RemoveRuleTagsAsync("products", It.IsAny<CancellationToken>()), Times.Once);
        _repo.Verify(r => r.GetOccupiedTagPairsAsync("products", It.IsAny<CancellationToken>()), Times.Once);
        requestedNames.Should().BeEquivalentTo(new[] { "products" }); // "events" excluded by scope
        added!.Should().OnlyContain(t => t.TagId == 10);
        result.PhotosUpdated.Should().Be(1);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~ReapplyRulesHandlerTests"`
Expected: BUILD FAILS or tests FAIL — the handler still calls the (about-to-be-removed) `ReapplyRulesAsync` and does not call the new primitives.

- [ ] **Step 3: Rewrite the handler `Handle` method**

Replace the **entire** contents of `ReapplyRulesHandler.cs` with:

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Anela.Heblo.Application.Features.Photobank.Services;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Photobank;
using MediatR;

namespace Anela.Heblo.Application.Features.Photobank.UseCases.ReapplyRules
{
    public class ReapplyRulesHandler : IRequestHandler<ReapplyRulesRequest, ReapplyRulesResponse>
    {
        private readonly IPhotobankRepository _repository;
        private readonly IPhotobankTagsCache _cache;

        public ReapplyRulesHandler(IPhotobankRepository repository, IPhotobankTagsCache cache)
        {
            _repository = repository;
            _cache = cache;
        }

        public async Task<ReapplyRulesResponse> Handle(ReapplyRulesRequest request, CancellationToken cancellationToken)
        {
            var allRules = await _repository.GetRulesAsync(cancellationToken);

            string? scopeToTagName = null;
            if (request.RuleId.HasValue)
            {
                var rule = allRules.FirstOrDefault(r => r.Id == request.RuleId.Value);
                if (rule == null)
                    return new ReapplyRulesResponse(ErrorCodes.PhotobankRuleNotFound);

                scopeToTagName = rule.TagName.ToLowerInvariant();
            }

            var activeRules = allRules.Where(r => r.IsActive).ToList();

            // Remove existing Rule-sourced tags (scoped) and commit first. Committing the
            // deletions detaches them before we re-add the same (PhotoId, TagId) pairs,
            // avoiding the EF change-tracker collision on no-op re-applies (shared composite PK).
            // This is also unconditional: the previous implementation always committed the
            // removal (the handler saved even when the repository returned 0), so removing
            // before the empty-rule short-circuit preserves behavior.
            await _repository.RemoveRuleTagsAsync(scopeToTagName, cancellationToken);
            await _repository.SaveChangesAsync(cancellationToken);

            var ruleTagNames = activeRules
                .Select(r => r.TagName.ToLowerInvariant())
                .Distinct()
                .ToList();

            if (scopeToTagName != null)
                ruleTagNames = ruleTagNames.Where(n => n == scopeToTagName).ToList();

            if (ruleTagNames.Count == 0)
            {
                _cache.Invalidate();
                return new ReapplyRulesResponse { PhotosUpdated = 0 };
            }

            var occupied = await _repository.GetOccupiedTagPairsAsync(scopeToTagName, cancellationToken);
            var tagIdsByName = await _repository.GetOrCreateTagsAsync(ruleTagNames, cancellationToken);
            var photos = await _repository.GetAllPhotosAsync(cancellationToken);

            var addedPairs = new HashSet<(int PhotoId, int TagId)>();
            var newPhotoTags = new List<PhotoTag>();
            var now = DateTime.UtcNow;
            var photosUpdated = 0;

            foreach (var photo in photos)
            {
                var allMatchingTagNames = TagRuleMatcher.GetMatchingTags(photo.FolderPath, photo.FileName, activeRules);
                var matchingTagNames = scopeToTagName != null
                    ? (IReadOnlyList<string>)allMatchingTagNames.Where(n => n == scopeToTagName).ToList()
                    : allMatchingTagNames;

                if (matchingTagNames.Count == 0)
                    continue;

                var tagsUpdated = false;
                foreach (var tagName in matchingTagNames)
                {
                    if (!tagIdsByName.TryGetValue(tagName, out var tagId))
                        continue;

                    var pair = (photo.Id, tagId);
                    if (!addedPairs.Add(pair))
                        continue;

                    if (occupied.Contains(pair))
                        continue;

                    newPhotoTags.Add(new PhotoTag
                    {
                        PhotoId = photo.Id,
                        TagId = tagId,
                        Source = PhotoTagSource.Rule,
                        CreatedAt = now,
                    });
                    tagsUpdated = true;
                }

                if (tagsUpdated)
                    photosUpdated++;
            }

            await _repository.AddPhotoTagsAsync(newPhotoTags, cancellationToken);
            await _repository.SaveChangesAsync(cancellationToken);
            _cache.Invalidate();

            return new ReapplyRulesResponse { PhotosUpdated = photosUpdated };
        }
    }
}
```

> The old `ReapplyRulesAsync` on the repository is now unused but still present — it is removed in Task 8. The solution still compiles at this point.

- [ ] **Step 4: Run the handler unit tests to verify they pass**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~ReapplyRulesHandlerTests"`
Expected: PASS (6 passed).

- [ ] **Step 5: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Photobank/UseCases/ReapplyRules/ReapplyRulesHandler.cs \
        backend/test/Anela.Heblo.Tests/Features/Photobank/ReapplyRulesHandlerTests.cs
git commit -m "refactor: move ReapplyRules orchestration into ReapplyRulesHandler"
```

---

## Task 7: Fix the `ReapplyRules` test in `PhotobankTagsCacheInvalidationTests`

**Files:**
- Modify: `backend/test/Anela.Heblo.Tests/Features/Photobank/PhotobankTagsCacheInvalidationTests.cs:202-213`

This test currently mocks `ReapplyRulesAsync` (line 206), which is removed in Task 8. Update it to the new primitive sequence. With an empty rules list, the handler removes (committing) → `ruleTagNames` empty → invalidates → returns 0, so we only need to verify `Invalidate()` is called once.

- [ ] **Step 1: Replace the `ReapplyRules_InvalidatesCache_AfterSave` test method**

Replace lines 202–213 (the `[Fact] ReapplyRules_InvalidatesCache_AfterSave` method) with:

```csharp
    [Fact]
    public async Task ReapplyRules_InvalidatesCache_AfterSave()
    {
        _repo.Setup(r => r.GetRulesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new List<TagRule>());
        _repo.Setup(r => r.RemoveRuleTagsAsync(It.IsAny<string?>(), It.IsAny<CancellationToken>()))
             .Returns(Task.CompletedTask);
        _repo.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
             .Returns(Task.CompletedTask);

        var handler = new ReapplyRulesHandler(_repo.Object, _cache.Object);
        await handler.Handle(new ReapplyRulesRequest(), CancellationToken.None);

        _cache.Verify(c => c.Invalidate(), Times.Once);
    }
```

- [ ] **Step 2: Run the cache invalidation tests to verify they pass**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~PhotobankTagsCacheInvalidationTests"`
Expected: PASS (all cache-invalidation tests green).

- [ ] **Step 3: Commit**

```bash
git add backend/test/Anela.Heblo.Tests/Features/Photobank/PhotobankTagsCacheInvalidationTests.cs
git commit -m "test: update ReapplyRules cache-invalidation test to new primitive sequence"
```

---

## Task 8: Remove `ReapplyRulesAsync` from the interface and implementation

**Files:**
- Modify: `backend/src/Anela.Heblo.Domain/Features/Photobank/IPhotobankRepository.cs:52-53`
- Modify: `backend/src/Anela.Heblo.Application/Features/Photobank/PhotobankRepository.cs:273-372`

- [ ] **Step 1: Confirm no remaining references**

Run: `grep -rn "ReapplyRulesAsync" backend/src backend/test`
Expected: **no matches** in `backend/src` or `backend/test` (only the interface/impl lines about to be deleted should appear; if any test still references it, fix that test first).

- [ ] **Step 2: Remove the interface declaration**

In `IPhotobankRepository.cs`, delete the `// Reapply rules` comment and its method (lines 52–53):

```csharp
        // Reapply rules
        Task<int> ReapplyRulesAsync(List<TagRule> allRules, string? scopeToTagName, CancellationToken cancellationToken);
```

- [ ] **Step 3: Remove the implementation**

In `PhotobankRepository.cs`, delete the `// Reapply rules` comment and the entire `ReapplyRulesAsync` method (lines 273–372, from `// Reapply rules` through the closing brace of the method ending at `return photosUpdated; }`).

- [ ] **Step 4: Verify the solution builds (the `TagRuleMatcher` reference is now gone from the repository)**

Run: `dotnet build backend/src/Anela.Heblo.Application/Anela.Heblo.Application.csproj`
Expected: BUILD SUCCEEDED, no warnings about unused usings. The `using Anela.Heblo.Domain.Features.Photobank;` at the top of `PhotobankRepository.cs` stays (other types — `Photo`, `Tag`, `PhotoTag`, `TagRule`, `PhotoTagSource` — still use it). `System.Text.RegularExpressions` (used by `BuildFilterQuery`) also stays.

- [ ] **Step 5: Run the full Photobank test suite**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~Features.Photobank"`
Expected: PASS (all Photobank tests green).

- [ ] **Step 6: Commit**

```bash
git add backend/src/Anela.Heblo.Domain/Features/Photobank/IPhotobankRepository.cs \
        backend/src/Anela.Heblo.Application/Features/Photobank/PhotobankRepository.cs
git commit -m "refactor: remove ReapplyRulesAsync from Photobank repository surface"
```

---

## Task 9: End-to-end behavior-preservation tests against a real `ApplicationDbContext`

**Files:**
- Test: `backend/test/Anela.Heblo.Tests/Features/Photobank/ReapplyRulesBehaviorPreservationTests.cs` (new file)

This is the **binding gate** for behavior preservation (FR-4). It wires the real `PhotobankRepository` + real `ReapplyRulesHandler` over an InMemory `ApplicationDbContext`, with a mocked `IPhotobankTagsCache`.

- [ ] **Step 1: Write the end-to-end tests (creates the new file)**

Create `backend/test/Anela.Heblo.Tests/Features/Photobank/ReapplyRulesBehaviorPreservationTests.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Anela.Heblo.Application.Features.Photobank;
using Anela.Heblo.Application.Features.Photobank.Services;
using Anela.Heblo.Application.Features.Photobank.UseCases.ReapplyRules;
using Anela.Heblo.Domain.Features.Photobank;
using Anela.Heblo.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Photobank;

public class ReapplyRulesBehaviorPreservationTests : IDisposable
{
    private readonly ApplicationDbContext _context;
    private readonly ReapplyRulesHandler _handler;

    public ReapplyRulesBehaviorPreservationTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        _context = new ApplicationDbContext(options);
        var repository = new PhotobankRepository(_context);
        var cache = new Mock<IPhotobankTagsCache>();
        _handler = new ReapplyRulesHandler(repository, cache.Object);
    }

    public void Dispose() => _context.Dispose();

    private async Task<List<PhotoTag>> AllPhotoTagsAsync() =>
        await _context.PhotoTags.AsNoTracking().ToListAsync(CancellationToken.None);

    [Fact]
    public async Task ManualTagWins_RuleTagNotInsertedOverSharedPk()
    {
        // Arrange — photo matches the "products" rule, but a Manual tag already owns (1, 10).
        _context.Photos.Add(new Photo { Id = 1, SharePointFileId = "sp-1", FolderPath = "Products/A", FileName = "a.jpg", ModifiedAt = DateTime.UtcNow });
        _context.PhotobankTags.Add(new Tag { Id = 10, Name = "products" });
        _context.PhotoTags.Add(new PhotoTag { PhotoId = 1, TagId = 10, Source = PhotoTagSource.Manual, CreatedAt = DateTime.UtcNow });
        _context.PhotobankTagRules.Add(new TagRule { Id = 1, PathPattern = "Products", TagName = "products", IsActive = true, SortOrder = 0 });
        await _context.SaveChangesAsync(CancellationToken.None);

        // Act
        var result = await _handler.Handle(new ReapplyRulesRequest(), CancellationToken.None);

        // Assert — the pair stays Manual, no Rule row added, photo not counted.
        result.PhotosUpdated.Should().Be(0);
        var tags = await AllPhotoTagsAsync();
        tags.Should().ContainSingle();
        tags[0].Source.Should().Be(PhotoTagSource.Manual);
    }

    [Fact]
    public async Task DuplicateMatch_AddsOneRow_PhotosUpdatedCountsPhotosNotTags()
    {
        // Arrange — photo 1 matches two rules producing the SAME tag; photo 2 matches two
        // rules producing DIFFERENT tags. photosUpdated should be 2 (photos), rows added = 3.
        _context.Photos.AddRange(
            new Photo { Id = 1, SharePointFileId = "sp-1", FolderPath = "Products/A", FileName = "a.jpg", ModifiedAt = DateTime.UtcNow },
            new Photo { Id = 2, SharePointFileId = "sp-2", FolderPath = "Products/Events", FileName = "b.jpg", ModifiedAt = DateTime.UtcNow });
        _context.PhotobankTagRules.AddRange(
            new TagRule { Id = 1, PathPattern = "Products", TagName = "products", IsActive = true, SortOrder = 0 },
            new TagRule { Id = 2, PathPattern = "A", TagName = "products", IsActive = true, SortOrder = 1 }, // dup tag for photo 1
            new TagRule { Id = 3, PathPattern = "Events", TagName = "events", IsActive = true, SortOrder = 2 }); // distinct tag for photo 2
        await _context.SaveChangesAsync(CancellationToken.None);

        // Act
        var result = await _handler.Handle(new ReapplyRulesRequest(), CancellationToken.None);

        // Assert
        result.PhotosUpdated.Should().Be(2);
        var tags = await AllPhotoTagsAsync();
        tags.Should().HaveCount(3);
        tags.Where(t => t.PhotoId == 1).Should().ContainSingle(); // dedup
        tags.Where(t => t.PhotoId == 2).Should().HaveCount(2);
        tags.Should().OnlyContain(t => t.Source == PhotoTagSource.Rule);
    }

    [Fact]
    public async Task EmptyActiveRules_RemovesAllRuleTags_AndReturnsZero()
    {
        // Arrange — pre-existing Rule + Manual tags; the only rule is INACTIVE.
        // Current behavior: removal is committed (handler always saved), so all Rule tags go.
        _context.Photos.Add(new Photo { Id = 1, SharePointFileId = "sp-1", FolderPath = "Products/A", FileName = "a.jpg", ModifiedAt = DateTime.UtcNow });
        _context.PhotobankTags.AddRange(
            new Tag { Id = 10, Name = "products" },
            new Tag { Id = 11, Name = "manual" });
        _context.PhotoTags.AddRange(
            new PhotoTag { PhotoId = 1, TagId = 10, Source = PhotoTagSource.Rule, CreatedAt = DateTime.UtcNow },
            new PhotoTag { PhotoId = 1, TagId = 11, Source = PhotoTagSource.Manual, CreatedAt = DateTime.UtcNow });
        _context.PhotobankTagRules.Add(new TagRule { Id = 1, PathPattern = "Products", TagName = "products", IsActive = false, SortOrder = 0 });
        await _context.SaveChangesAsync(CancellationToken.None);

        // Act
        var result = await _handler.Handle(new ReapplyRulesRequest(), CancellationToken.None);

        // Assert
        result.PhotosUpdated.Should().Be(0);
        var tags = await AllPhotoTagsAsync();
        tags.Should().ContainSingle();
        tags[0].Source.Should().Be(PhotoTagSource.Manual); // Rule tag removed, Manual preserved
    }

    [Fact]
    public async Task ScopedReapply_OnlyTouchesTargetRuleTag()
    {
        // Arrange — photo matches both rules. A pre-existing "events" Rule tag must survive a
        // scoped re-apply of the "products" rule; a "products" Rule tag is recomputed.
        _context.Photos.Add(new Photo { Id = 1, SharePointFileId = "sp-1", FolderPath = "Products/Events", FileName = "a.jpg", ModifiedAt = DateTime.UtcNow });
        _context.PhotobankTags.AddRange(
            new Tag { Id = 10, Name = "products" },
            new Tag { Id = 11, Name = "events" });
        _context.PhotoTags.Add(new PhotoTag { PhotoId = 1, TagId = 11, Source = PhotoTagSource.Rule, CreatedAt = DateTime.UtcNow });
        _context.PhotobankTagRules.AddRange(
            new TagRule { Id = 1, PathPattern = "Products", TagName = "products", IsActive = true, SortOrder = 0 },
            new TagRule { Id = 2, PathPattern = "Events", TagName = "events", IsActive = true, SortOrder = 1 });
        await _context.SaveChangesAsync(CancellationToken.None);

        // Act — scope to rule 1 ("products")
        var result = await _handler.Handle(new ReapplyRulesRequest { RuleId = 1 }, CancellationToken.None);

        // Assert
        result.PhotosUpdated.Should().Be(1);
        var tags = await AllPhotoTagsAsync();
        tags.Should().HaveCount(2);
        tags.Should().Contain(t => t.TagId == 10); // products added
        tags.Should().Contain(t => t.TagId == 11); // events untouched
    }

    [Fact]
    public async Task DoubleApply_NoNewTags_IsIdempotent_AndDoesNotThrow()
    {
        // Arrange — tag already exists, photo matches, a Rule tag already present.
        // This exercises the delete-then-re-add change-tracker hazard on the no-new-tags path.
        _context.Photos.Add(new Photo { Id = 1, SharePointFileId = "sp-1", FolderPath = "Products/A", FileName = "a.jpg", ModifiedAt = DateTime.UtcNow });
        _context.PhotobankTags.Add(new Tag { Id = 10, Name = "products" });
        _context.PhotoTags.Add(new PhotoTag { PhotoId = 1, TagId = 10, Source = PhotoTagSource.Rule, CreatedAt = DateTime.UtcNow });
        _context.PhotobankTagRules.Add(new TagRule { Id = 1, PathPattern = "Products", TagName = "products", IsActive = true, SortOrder = 0 });
        await _context.SaveChangesAsync(CancellationToken.None);

        // Act — apply twice; neither should throw, and the result rows stay identical.
        var first = await _handler.Handle(new ReapplyRulesRequest(), CancellationToken.None);
        var second = await _handler.Handle(new ReapplyRulesRequest(), CancellationToken.None);

        // Assert
        first.PhotosUpdated.Should().Be(1);
        second.PhotosUpdated.Should().Be(1);
        var tags = await AllPhotoTagsAsync();
        tags.Should().ContainSingle();
        tags[0].Should().BeEquivalentTo(new { PhotoId = 1, TagId = 10, Source = PhotoTagSource.Rule },
            o => o.ExcludingMissingMembers());
    }
}
```

- [ ] **Step 2: Run the end-to-end tests to verify they pass**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~ReapplyRulesBehaviorPreservationTests"`
Expected: PASS (5 passed). If `DoubleApply_NoNewTags_IsIdempotent_AndDoesNotThrow` throws *"another instance with the same key value is already being tracked"*, the removal is not being committed before the re-add — re-check Task 6 Step 3 (the `RemoveRuleTagsAsync` → `SaveChangesAsync` must run before the add phase).

- [ ] **Step 3: Commit**

```bash
git add backend/test/Anela.Heblo.Tests/Features/Photobank/ReapplyRulesBehaviorPreservationTests.cs
git commit -m "test: add end-to-end behavior-preservation tests for ReapplyRules refactor"
```

---

## Task 10: Final validation

**Files:** none (validation only).

- [ ] **Step 1: Build the backend**

Run: `dotnet build backend/Anela.Heblo.sln` (or the solution file if named differently — confirm with `ls backend/*.sln`)
Expected: BUILD SUCCEEDED, 0 errors.

- [ ] **Step 2: Format check**

Run: `dotnet format backend/Anela.Heblo.sln --verify-no-changes`
Expected: no formatting changes required. If it reports changes, run `dotnet format backend/Anela.Heblo.sln` and commit them.

- [ ] **Step 3: Run the full backend test suite**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj`
Expected: PASS (all tests green, including all `Features.Photobank` tests).

- [ ] **Step 4: Final reference sweep**

Run: `grep -rn "ReapplyRulesAsync" backend`
Expected: **no matches** anywhere under `backend/` (production or tests).

- [ ] **Step 5: Commit any formatting fixes (if Step 2 produced changes)**

```bash
git add -A
git commit -m "chore: dotnet format after ReapplyRules refactor"
```

---

## Self-Review

**Spec coverage:**
- FR-1 (remove `ReapplyRulesAsync` from interface + impl, no `TagRuleMatcher` in repo) → Task 8.
- FR-2 (five thin primitives) → Tasks 1–5 (`GetAllPhotosAsync`, `RemoveRuleTagsAsync`, `GetOccupiedTagPairsAsync`, `AddPhotoTagsAsync`, `GetOrCreateTagsAsync`).
- FR-3 (orchestration in handler, behavior-preserving) → Task 6.
- FR-4 (rewrite handler tests + per-primitive repo tests + end-to-end behavior-preservation test incl. no-new-tags double-apply) → Tasks 1–5 (repo tests), Task 6 (handler tests), Task 9 (e2e incl. `DoubleApply_NoNewTags`).
- Arch-review amendments: Decision 1 (tracked `RemoveRange`, not `ExecuteDeleteAsync`) → Task 2; Decision 2 (flush removals before adds) → Task 6 Step 3 **with the corrected early-return placement** documented at the top of the plan and verified by Task 9's `EmptyActiveRules` and `DoubleApply` tests; Decision 3 (batch `GetOrCreateTagsAsync`) → Task 5; Decision 4 (load-all is preserved, batching is a non-goal) → `GetAllPhotosAsync` in Task 1.

**Placeholder scan:** No TBD/TODO/"add error handling"/"similar to Task N". Every code step shows full code; every run step states the expected outcome.

**Type consistency:** `GetOrCreateTagsAsync` returns `IReadOnlyDictionary<string, int>` and the handler consumes it via `TryGetValue(tagName, out var tagId)` (int) — consistent across Task 5 and Task 6. `GetOccupiedTagPairsAsync` returns `HashSet<(int PhotoId, int TagId)>` and the handler builds `pair = (photo.Id, tagId)` checked with `occupied.Contains(pair)` — consistent. `AddPhotoTagsAsync(IEnumerable<PhotoTag>)` matches the handler passing `List<PhotoTag>`. `RemoveRuleTagsAsync(string?)` / `GetOccupiedTagPairsAsync(string?)` / `GetOrCreateTagsAsync(IReadOnlyCollection<string>)` signatures match their interface declarations and handler call sites. Method names are stable across all tasks.

**Behavioral fidelity check:** removal committed before the empty-`ruleTagNames` short-circuit (preserves the current "deactivate all rules ⇒ rule tags cleared, returns 0" behavior); `ToLowerInvariant()` preserved on rule tag names and scope; `addedPairs` dedup and `occupied` precedence preserved; `photosUpdated` counts photos (not tags); cache invalidated exactly once in every non-error path.
