# Photobank Batch Tag Upsert N+1 Query Fix Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace `PhotobankIndexJob.UpsertPhotoBatchAsync`'s per-photo/per-pair database round-trips in its tag-reconciliation phase with two batch-scoped bulk queries plus in-memory lookups, so the phase issues a constant number of queries per batch instead of scaling with batch size or rule count.

**Architecture:** Add one new additive repository method (`GetPhotoTagsByPhotosAndSourceAsync`, photo-ID-set-scoped, mirroring the existing `RemovePhotoTagsBySourceAsync` pattern) to `IPhotobankPhotoTagRepository`/`PhotobankPhotoTagRepository`. Reuse the existing `GetOccupiedTagPairsAsync` as-is (unscoped by photo ID, exactly as `ReapplyRulesHandler` already calls it — see Task 1 for why no photo-ID scoping is added). In `PhotobankIndexJob.UpsertPhotoBatchAsync`, call both bulk methods once before the per-photo loop, build a `Dictionary<int, List<PhotoTag>>` and a `HashSet<(int PhotoId, int TagId)>`, and replace the loop's two per-photo/per-pair repository calls with dictionary/HashSet lookups plus a new in-loop `addedPairsThisBatch` HashSet that replaces the within-batch duplicate-guard role `PhotoTagExistsAsync` used to (coincidentally) serve.

**Tech Stack:** .NET 8, EF Core (InMemory provider for repository tests), MediatR (unrelated to this change), xUnit + Moq + FluentAssertions for tests.

---

## File Structure

- **Modify** `backend/src/Anela.Heblo.Domain/Features/Photobank/IPhotobankPhotoTagRepository.cs` — add one new method signature: `GetPhotoTagsByPhotosAndSourceAsync(IReadOnlyCollection<int> photoIds, PhotoTagSource source, CancellationToken cancellationToken)`.
- **Modify** `backend/src/Anela.Heblo.Persistence/Photobank/PhotobankPhotoTagRepository.cs` — implement the new method as a single `WHERE PhotoId IN (...) AND Source == ...` query grouped by `PhotoId`, following the same style as `RemovePhotoTagsBySourceAsync` (lines 83–89) and `GetPhotoTagsByPhotoAndSourceAsync` (lines 70–75) already in that file.
- **Modify** `backend/test/Anela.Heblo.Tests/Features/Photobank/PhotobankRepositoryReapplyPrimitivesTests.cs` — add unit tests for the new repository method against an EF Core InMemory `ApplicationDbContext`, following the file's existing convention (see `GetOccupiedTagPairsAsync_unscoped_returnsOnlyNonRulePairs`, lines 139–159).
- **Modify** `backend/src/Anela.Heblo.Application/Features/Photobank/Infrastructure/Jobs/PhotobankIndexJob.cs` — replace the body of the per-photo loop at lines 240–259 (inside `UpsertPhotoBatchAsync`) with the bulk-preload + in-memory-lookup version. Everything above line 240 is unchanged.
- **Modify** `backend/test/Anela.Heblo.Tests/Features/Photobank/PhotobankIndexJobTests.cs` — update every test that currently mocks `GetPhotoTagsByPhotoAndSourceAsync` and/or `PhotoTagExistsAsync` on `_photoTagRepoMock` to instead mock `GetPhotoTagsByPhotosAndSourceAsync` and `GetOccupiedTagPairsAsync`. Exact call sites needing this update (confirmed by reading the file): lines 100, 215, 300, 534, 628, 723, 822, 934, 1053 (`GetPhotoTagsByPhotoAndSourceAsync` setups) and lines 112, 312, 542, 946, 1068 (`PhotoTagExistsAsync` setups), plus the `Times.Once` verification at line 978 (`GetPhotoTagsByPhotoAndSourceAsync` → becomes a verification on `GetPhotoTagsByPhotosAndSourceAsync`, still `Times.Once` since it is now called once per batch, same cardinality as before in every existing test's single-batch scenario).

No files are created; no files are deleted.

---

### task: add-bulk-tag-lookup-repository-method

**Files:**
- Modify: `backend/src/Anela.Heblo.Domain/Features/Photobank/IPhotobankPhotoTagRepository.cs`
- Modify: `backend/src/Anela.Heblo.Persistence/Photobank/PhotobankPhotoTagRepository.cs`
- Test: `backend/test/Anela.Heblo.Tests/Features/Photobank/PhotobankRepositoryReapplyPrimitivesTests.cs`

This task adds the one new repository primitive the fix needs. It does **not** touch `PhotobankIndexJob` yet — that is Task 2. `GetOccupiedTagPairsAsync` is reused completely unmodified (per architect Decision 1 discussion): the existing signature `GetOccupiedTagPairsAsync(string? scopeToTagName, CancellationToken)` already returns `HashSet<(int PhotoId, int TagId)>` for the *entire* non-Rule-tag table (or a tag-name-scoped subset). No photo-ID-scoped overload is added — the batch job will call it exactly like `ReapplyRulesHandler` already does (unscoped by photo ID) and rely on `IN`-clause-free full retrieval; this keeps the change purely additive to the interface (Decision 2 in `arch-review.r1.md`) and avoids widening this task's blast radius. If a future profiling pass shows this is too costly at current `PhotoTags` table size, that is out of scope here (see spec's "Out of Scope" section).

- [ ] **Step 1: Write the failing repository test**

Add to `backend/test/Anela.Heblo.Tests/Features/Photobank/PhotobankRepositoryReapplyPrimitivesTests.cs`, right after `GetOccupiedTagPairsAsync_scoped_filtersByTagName` (after line 179, before `AddPhotoTagsAsync_stagesRows_persistedAfterSave`):

```csharp
    [Fact]
    public async System.Threading.Tasks.Task GetPhotoTagsByPhotosAndSourceAsync_multiplePhotos_returnsOnlyMatchingSourceGroupedByPhotoId()
    {
        // Arrange
        _context.Photos.AddRange(
            new Photo { Id = 1, SharePointFileId = "sp-1", FileName = "a.jpg", FolderPath = "P", ModifiedAt = DateTime.UtcNow },
            new Photo { Id = 2, SharePointFileId = "sp-2", FileName = "b.jpg", FolderPath = "P", ModifiedAt = DateTime.UtcNow },
            new Photo { Id = 3, SharePointFileId = "sp-3", FileName = "c.jpg", FolderPath = "P", ModifiedAt = DateTime.UtcNow });
        _context.PhotobankTags.AddRange(
            new Tag { Id = 10, Name = "products" },
            new Tag { Id = 11, Name = "events" },
            new Tag { Id = 12, Name = "manualtag" });
        _context.PhotoTags.AddRange(
            new PhotoTag { PhotoId = 1, TagId = 10, Source = PhotoTagSource.Rule, CreatedAt = DateTime.UtcNow },
            new PhotoTag { PhotoId = 1, TagId = 11, Source = PhotoTagSource.Rule, CreatedAt = DateTime.UtcNow },
            new PhotoTag { PhotoId = 2, TagId = 10, Source = PhotoTagSource.Rule, CreatedAt = DateTime.UtcNow },
            new PhotoTag { PhotoId = 2, TagId = 12, Source = PhotoTagSource.Manual, CreatedAt = DateTime.UtcNow },
            // Photo 3 has no Rule tags at all, and is NOT in the requested photoIds set below.
            new PhotoTag { PhotoId = 3, TagId = 10, Source = PhotoTagSource.Rule, CreatedAt = DateTime.UtcNow });
        await _context.SaveChangesAsync(CancellationToken.None);

        // Act — only ask for photos 1 and 2; photo 3's Rule tag must not leak in even though
        // it matches the source filter, because it is outside the requested photo-ID set.
        var result = await _photoTagRepository.GetPhotoTagsByPhotosAndSourceAsync(
            new[] { 1, 2 }, PhotoTagSource.Rule, CancellationToken.None);

        // Assert
        result.Should().HaveCount(2);
        result[1].Select(pt => pt.TagId).Should().BeEquivalentTo(new[] { 10, 11 });
        result[2].Select(pt => pt.TagId).Should().BeEquivalentTo(new[] { 10 }); // Manual tag excluded
        result.Should().NotContainKey(3);
    }

    [Fact]
    public async System.Threading.Tasks.Task GetPhotoTagsByPhotosAndSourceAsync_emptyPhotoIds_returnsEmptyDictionaryWithoutQuerying()
    {
        // Act
        var result = await _photoTagRepository.GetPhotoTagsByPhotosAndSourceAsync(
            Array.Empty<int>(), PhotoTagSource.Rule, CancellationToken.None);

        // Assert
        result.Should().BeEmpty();
    }
```

- [ ] **Step 2: Run the tests to verify they fail (method does not exist yet)**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~PhotobankRepositoryReapplyPrimitivesTests"`
Expected: build FAILS — `PhotobankPhotoTagRepository` has no method `GetPhotoTagsByPhotosAndSourceAsync`.

- [ ] **Step 3: Add the method to the interface**

In `backend/src/Anela.Heblo.Domain/Features/Photobank/IPhotobankPhotoTagRepository.cs`, add a new line inside the interface body (after the existing `GetPhotoTagsByPhotoAndSourceAsync` line, i.e. after current line 15):

```csharp
        Task<IReadOnlyDictionary<int, List<PhotoTag>>> GetPhotoTagsByPhotosAndSourceAsync(IReadOnlyCollection<int> photoIds, PhotoTagSource source, CancellationToken cancellationToken);
```

The full interface file becomes:

```csharp
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Anela.Heblo.Domain.Features.Photobank
{
    public interface IPhotobankPhotoTagRepository
    {
        Task AddPhotoTagAsync(PhotoTag photoTag, CancellationToken cancellationToken);
        Task AddPhotoTagsAsync(IEnumerable<PhotoTag> photoTags, CancellationToken cancellationToken);
        Task RemovePhotoTagAsync(int photoId, int tagId, CancellationToken cancellationToken);
        Task<bool> PhotoTagExistsAsync(int photoId, int tagId, CancellationToken cancellationToken);
        Task RemoveRuleTagsAsync(string? scopeToTagName, CancellationToken cancellationToken);
        Task<HashSet<(int PhotoId, int TagId)>> GetOccupiedTagPairsAsync(string? scopeToTagName, CancellationToken cancellationToken);
        Task<List<PhotoTag>> GetPhotoTagsByPhotoAndSourceAsync(int photoId, PhotoTagSource source, CancellationToken cancellationToken);
        Task<IReadOnlyDictionary<int, List<PhotoTag>>> GetPhotoTagsByPhotosAndSourceAsync(IReadOnlyCollection<int> photoIds, PhotoTagSource source, CancellationToken cancellationToken);
        Task RemovePhotoTagsAsync(IEnumerable<PhotoTag> photoTags, CancellationToken cancellationToken);
        Task RemovePhotoTagsBySourceAsync(IReadOnlyList<int> photoIds, PhotoTagSource source, CancellationToken cancellationToken);

        Task SaveChangesAsync(CancellationToken cancellationToken);
    }
}
```

`PhotoTagExistsAsync` and the singular `GetPhotoTagsByPhotoAndSourceAsync` are left in place untouched — they are not removed by this task (see architect Decision 2: additive-only interface change; `PhotoTagExistsAsync`'s removal from the job's call path happens in Task 2 but the interface method itself is not deleted since removing an interface member is a wider-blast-radius change than this fix needs, and no other repository implementation exists to worry about — grep confirms `PhotobankPhotoTagRepository` is the only implementer).

- [ ] **Step 4: Implement the method**

In `backend/src/Anela.Heblo.Persistence/Photobank/PhotobankPhotoTagRepository.cs`, add this method (after `GetPhotoTagsByPhotoAndSourceAsync`, i.e. after current line 75, before `RemovePhotoTagsAsync`):

```csharp
    public async Task<IReadOnlyDictionary<int, List<PhotoTag>>> GetPhotoTagsByPhotosAndSourceAsync(
        IReadOnlyCollection<int> photoIds, PhotoTagSource source, CancellationToken cancellationToken)
    {
        if (photoIds.Count == 0)
            return new Dictionary<int, List<PhotoTag>>();

        var rows = await _context.PhotoTags
            .Where(pt => photoIds.Contains(pt.PhotoId) && pt.Source == source)
            .ToListAsync(cancellationToken);

        return rows
            .GroupBy(pt => pt.PhotoId)
            .ToDictionary(g => g.Key, g => g.ToList());
    }
```

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~PhotobankRepositoryReapplyPrimitivesTests"`
Expected: PASS — all tests in the file, including the two new ones.

- [ ] **Step 6: Build the whole solution to confirm the new interface member doesn't break any other implementer**

Run: `dotnet build Anela.Heblo.sln`
Expected: Build succeeded, 0 errors. (`PhotobankPhotoTagRepository` is the only implementer of `IPhotobankPhotoTagRepository`, so no other class needs updating — confirmed by the architect review's grep.)

- [ ] **Step 7: Commit**

```bash
git add backend/src/Anela.Heblo.Domain/Features/Photobank/IPhotobankPhotoTagRepository.cs backend/src/Anela.Heblo.Persistence/Photobank/PhotobankPhotoTagRepository.cs backend/test/Anela.Heblo.Tests/Features/Photobank/PhotobankRepositoryReapplyPrimitivesTests.cs
git commit -m "feat(photobank): add batch-scoped GetPhotoTagsByPhotosAndSourceAsync repository method"
```

---

### task: refactor-upsertphotobatch-tag-reconciliation

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/Photobank/Infrastructure/Jobs/PhotobankIndexJob.cs:240-259`
- Modify: `backend/test/Anela.Heblo.Tests/Features/Photobank/PhotobankIndexJobTests.cs`

This task replaces the N+1 per-photo loop body in `UpsertPhotoBatchAsync` with the bulk-preload version, and updates every existing test's mocks so they still compile and pass with identical asserted outcomes (FR-3: behavior must be unchanged). This task depends on Task 1 (`GetPhotoTagsByPhotosAndSourceAsync` must already exist).

- [ ] **Step 1: Update all existing test mocks to the new bulk methods, without touching production code yet**

In `backend/test/Anela.Heblo.Tests/Features/Photobank/PhotobankIndexJobTests.cs`, every occurrence of:

```csharp
        _photoTagRepoMock
            .Setup(r => r.GetPhotoTagsByPhotoAndSourceAsync(It.IsAny<int>(), PhotoTagSource.Rule, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
```

(at lines 100, 215, 300, 534, 628, 723, 822, 934, 1053) becomes:

```csharp
        _photoTagRepoMock
            .Setup(r => r.GetPhotoTagsByPhotosAndSourceAsync(It.IsAny<IReadOnlyCollection<int>>(), PhotoTagSource.Rule, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<int, List<PhotoTag>>());
```

And every occurrence of:

```csharp
        _photoTagRepoMock
            .Setup(r => r.PhotoTagExistsAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
```

(at lines 542, 946, 1068, and line 112's `.ReturnsAsync(false)` sibling at line 313 which returns `true` — see next paragraph) becomes:

```csharp
        _photoTagRepoMock
            .Setup(r => r.GetOccupiedTagPairsAsync(It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HashSet<(int PhotoId, int TagId)>());
```

**Exception — `UpsertPhoto_WhenTagAlreadyExists_SkipsInsert` (lines 250–338):** this test's `PhotoTagExistsAsync` setup at line 312 returns `true` (not `false`) to simulate "this pair already exists, skip the insert." Since the new photo in this test is never persisted through a real `SaveChangesAsync` (it is mocked), its `Id` stays at the CLR default `0` for the whole test. Replace the line 311–313 setup with:

```csharp
        _photoTagRepoMock
            .Setup(r => r.GetOccupiedTagPairsAsync(It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HashSet<(int PhotoId, int TagId)> { (0, 42) });
```

(Tag id `42` comes from this test's own `GetOrCreateTagsAsync` setup at line 309 — `["produkty"] = 42` — unchanged.) The assertion at line 337 (`AddPhotoTagAsync` never called) is unchanged and must still pass.

**Also update the verification at line 978** (inside `UpsertPhotoBatch_DuplicateSharePointFileIdMatchingDifferentTagRules_OnlyLastItemsTagsSurvive`):

```csharp
        _photoTagRepoMock.Verify(r => r.GetPhotoTagsByPhotoAndSourceAsync(It.IsAny<int>(), PhotoTagSource.Rule, It.IsAny<CancellationToken>()), Times.Once);
```

becomes:

```csharp
        _photoTagRepoMock.Verify(r => r.GetPhotoTagsByPhotosAndSourceAsync(It.IsAny<IReadOnlyCollection<int>>(), PhotoTagSource.Rule, It.IsAny<CancellationToken>()), Times.Once);
```

This is still `Times.Once` — every existing test drives exactly one call to `UpsertPhotoBatchAsync` (one Graph delta page, one batch), so the bulk method is still called exactly once per test, same cardinality as the old per-photo method was previously asserted to be called once (because that test's whole point is "only one distinct photo is processed despite two raw items").

**`UpsertPhotoBatch_DuplicateSharePointFileIdMatchingSameTagRule_AppliesTagOnceWithNoDuplicateKeyRisk` (lines 986–1099)** — update its `PhotoTagExistsAsync` setup (lines 1067–1069) to the `GetOccupiedTagPairsAsync` form above (empty set, since this test's whole point is that no DB-visible occupied pair exists yet — the guard must come from the new in-loop `addedPairsThisBatch`, not from this preload). Its final assertion at line 1098 (`AddPhotoTagAsync` called exactly once for tag id 99) is unchanged and is the test that will catch a regression if the new in-loop duplicate guard (Step 3 below) is missing or wrong.

- [ ] **Step 2: Run tests to verify they now fail at the call site (compiles against interface, but job still calls the old methods so behavior/mock-setup mismatch causes failures)**

Run: `dotnet build backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj`
Expected: build FAILS or the subsequent test run fails — `PhotobankIndexJob` still calls `GetPhotoTagsByPhotoAndSourceAsync`/`PhotoTagExistsAsync`, which the mocks no longer stub (Moq's default `ReturnsAsync` for an un-stubbed method throws or returns default, causing null-reference/assertion failures). This step is a checkpoint confirming the test changes are exercised, not a strict compile failure — proceed to Step 3 regardless of the exact failure mode, since the goal is just "old mocks removed, new mocks not yet consumed."

- [ ] **Step 3: Replace the per-photo loop body in `PhotobankIndexJob.UpsertPhotoBatchAsync`**

In `backend/src/Anela.Heblo.Application/Features/Photobank/Infrastructure/Jobs/PhotobankIndexJob.cs`, replace lines 240–259 (the `foreach (var (photo, tagNames) in tagNamesByPhoto)` loop) with:

```csharp
        var batchPhotoIds = tagNamesByPhoto.Keys.Select(p => p.Id).ToList();

        var existingRuleTagsByPhoto = batchPhotoIds.Count > 0
            ? await _photoTagRepository.GetPhotoTagsByPhotosAndSourceAsync(batchPhotoIds, PhotoTagSource.Rule, ct)
            : new Dictionary<int, List<PhotoTag>>();

        var occupiedNonRulePairs = await _photoTagRepository.GetOccupiedTagPairsAsync(scopeToTagName: null, ct);

        var addedPairsThisBatch = new HashSet<(int PhotoId, int TagId)>();

        foreach (var (photo, tagNames) in tagNamesByPhoto)
        {
            // Re-apply rule tags: remove existing Rule-source tags, add new ones.
            // Both existing-tags-to-remove and occupied-pairs-to-skip now come from the two
            // bulk queries preloaded above, not from per-photo/per-pair round-trips.
            var existingRuleTags = existingRuleTagsByPhoto.TryGetValue(photo.Id, out var tags)
                ? tags
                : new List<PhotoTag>();
            await _photoTagRepository.RemovePhotoTagsAsync(existingRuleTags, ct);

            foreach (var tagName in tagNames)
            {
                if (!tagIdsByName.TryGetValue(tagName, out var tagId)) continue;

                var pair = (photo.Id, tagId);
                if (occupiedNonRulePairs.Contains(pair)) continue;
                // addedPairsThisBatch replaces the within-batch duplicate-guard role that
                // PhotoTagExistsAsync's real-DB check used to (coincidentally) serve: that
                // check could never see this batch's own unflushed inserts, so this explicit
                // in-memory guard is required now that the per-pair DB check is gone.
                if (!addedPairsThisBatch.Add(pair)) continue;

                await _photoTagRepository.AddPhotoTagAsync(new PhotoTag
                {
                    PhotoId = photo.Id,
                    TagId = tagId,
                    Source = PhotoTagSource.Rule,
                    CreatedAt = DateTime.UtcNow,
                }, ct);
            }
        }
```

Everything before this block (lines 1–239, including `tagNamesByPhoto` computation) and after it (the closing `await _photoTagRepository.SaveChangesAsync(ct);` at the former line 261) is unchanged.

- [ ] **Step 4: Run the full Photobank test file to verify all tests pass**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~PhotobankIndexJobTests"`
Expected: PASS — all tests in `PhotobankIndexJobTests.cs`, including `UpsertPhotoBatch_DuplicateSharePointFileIdMatchingSameTagRule_AppliesTagOnceWithNoDuplicateKeyRisk` and `UpsertPhoto_WhenTagAlreadyExists_SkipsInsert`.

- [ ] **Step 5: Add a mock-call-count assertion proving the query count is now O(1) per batch**

Add a new test to `PhotobankIndexJobTests.cs` (after `UpsertPhotoBatch_DuplicateSharePointFileIdMatchingSameTagRule_AppliesTagOnceWithNoDuplicateKeyRisk`, i.e. after the current final line 1099, before the closing class brace) that drives a batch with multiple *distinct* photos and asserts the bulk methods are called exactly once regardless of how many distinct photos/tags are in the batch — this is the regression test that would catch a reintroduced N+1:

```csharp
    [Fact]
    public async Task UpsertPhotoBatch_MultipleDistinctPhotosInOneBatch_CallsBulkTagQueriesExactlyOnceEach()
    {
        // Arrange — three distinct photos (distinct SharePointFileIds), each matching a rule,
        // in a single Graph delta batch. Before this fix, GetPhotoTagsByPhotoAndSourceAsync and
        // PhotoTagExistsAsync would each be called once per photo (and per matched tag). After
        // the fix, GetPhotoTagsByPhotosAndSourceAsync and GetOccupiedTagPairsAsync must each be
        // called exactly once for the whole batch, independent of the number of distinct photos.
        var root = new PhotobankIndexRoot
        {
            Id = 1,
            SharePointPath = "/sites/test/photos",
            DriveId = "drive-1",
            RootItemId = "root-item-1",
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
        };

        var tagRule = new TagRule
        {
            PathPattern = "Fotky/Produkty",
            TagName = "produkty",
            IsActive = true,
            SortOrder = 0,
        };

        var items = Enumerable.Range(1, 3).Select(i => new GraphPhotoItem
        {
            ItemId = $"file-{i}",
            Name = $"photo-{i}.jpg",
            FolderPath = "Fotky/Produkty",
            WebUrl = $"https://sharepoint.example.com/photo-{i}.jpg",
            FileSizeBytes = 1024,
            LastModifiedAt = DateTime.UtcNow,
            DriveId = "drive-1",
            IsDeleted = false,
        }).ToList();

        _rootRepoMock
            .Setup(r => r.GetActiveRootsWithDriveAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([root]);

        _tagRuleRepoMock
            .Setup(r => r.GetActiveTagRulesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([tagRule]);

        foreach (var item in items)
        {
            _photoRepoMock
                .Setup(r => r.GetPhotoBySharePointFileIdAsync(item.ItemId, It.IsAny<CancellationToken>()))
                .ReturnsAsync((Photo?)null);
        }

        _photoRepoMock
            .Setup(r => r.AddPhotoAsync(It.IsAny<Photo>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _photoTagRepoMock
            .Setup(r => r.GetPhotoTagsByPhotosAndSourceAsync(It.IsAny<IReadOnlyCollection<int>>(), PhotoTagSource.Rule, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<int, List<PhotoTag>>());

        _photoTagRepoMock
            .Setup(r => r.RemovePhotoTagsAsync(It.IsAny<IEnumerable<PhotoTag>>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _tagRepoMock
            .Setup(r => r.GetOrCreateTagsAsync(It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, int> { ["produkty"] = 42 });

        _photoTagRepoMock
            .Setup(r => r.GetOccupiedTagPairsAsync(It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HashSet<(int PhotoId, int TagId)>());

        _photoTagRepoMock
            .Setup(r => r.AddPhotoTagAsync(It.IsAny<PhotoTag>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _photoRepoMock
            .Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _photoTagRepoMock
            .Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _rootRepoMock
            .Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _graphServiceMock
            .Setup(g => g.GetDeltaAsync("drive-1", "root-item-1", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GraphDeltaResult
            {
                Items = items,
                NewDeltaLink = "https://graph.microsoft.com/v1.0/drives/drive-1/items/root-item-1/delta?token=abc",
            });

        // Act
        await _job.ExecuteAsync();

        // Assert — exactly one bulk call each, regardless of 3 distinct photos being processed.
        _photoTagRepoMock.Verify(r => r.GetPhotoTagsByPhotosAndSourceAsync(It.IsAny<IReadOnlyCollection<int>>(), PhotoTagSource.Rule, It.IsAny<CancellationToken>()), Times.Once);
        _photoTagRepoMock.Verify(r => r.GetOccupiedTagPairsAsync(It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Once);
        _photoTagRepoMock.Verify(r => r.PhotoTagExistsAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
        _photoTagRepoMock.Verify(r => r.GetPhotoTagsByPhotoAndSourceAsync(It.IsAny<int>(), It.IsAny<PhotoTagSource>(), It.IsAny<CancellationToken>()), Times.Never);
        _photoTagRepoMock.Verify(r => r.AddPhotoTagAsync(It.IsAny<PhotoTag>(), It.IsAny<CancellationToken>()), Times.Exactly(3));
    }
```

This test requires `using System.Linq;` in the test file for `Enumerable.Range` — check the top of `PhotobankIndexJobTests.cs` first; if `System.Linq` is not already imported, add `using System.Linq;` to its using block.

- [ ] **Step 6: Run the full Photobank test file again to verify the new test passes too**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~PhotobankIndexJobTests"`
Expected: PASS — all tests, including the new `UpsertPhotoBatch_MultipleDistinctPhotosInOneBatch_CallsBulkTagQueriesExactlyOnceEach`.

- [ ] **Step 7: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Photobank/Infrastructure/Jobs/PhotobankIndexJob.cs backend/test/Anela.Heblo.Tests/Features/Photobank/PhotobankIndexJobTests.cs
git commit -m "fix(photobank): bulk-preload tag reconciliation in UpsertPhotoBatchAsync to remove N+1 queries"
```

---

### task: full-validation-and-cleanup

**Files:** none new — this task runs the repository's standard validation gates against the two prior tasks' changes and fixes anything they surface.

- [ ] **Step 1: Run `dotnet format` to check/fix formatting**

Run: `dotnet format Anela.Heblo.sln --verify-no-changes`
Expected: no formatting violations. If violations are reported, run `dotnet format Anela.Heblo.sln`, review the diff is confined to the files touched by Tasks 1–2 (or pre-existing unrelated files — if so, do not commit those, per CLAUDE.md's "surgical changes" rule), and re-run `--verify-no-changes` to confirm.

- [ ] **Step 2: Full solution build**

Run: `dotnet build Anela.Heblo.sln`
Expected: Build succeeded, 0 warnings introduced, 0 errors.

- [ ] **Step 3: Run the full backend test suite for the touched project**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj`
Expected: PASS — all tests in the project, not just the Photobank subset, to catch any unexpected cross-feature regression (there should be none, since only Photobank files were touched).

- [ ] **Step 4: Self-review against the spec (FR-1, FR-2, FR-3, NFR-1, NFR-2)**

Manually confirm, by re-reading the final diff of `PhotobankIndexJob.cs` and `PhotobankPhotoTagRepository.cs`:
- FR-1: exactly one call to `GetPhotoTagsByPhotosAndSourceAsync` per batch — confirmed by the Step 5 test in Task 2.
- FR-2: exactly one call to `GetOccupiedTagPairsAsync` per batch, in-memory `HashSet` lookups replace `PhotoTagExistsAsync` — confirmed by the same test.
- FR-3 / architect's added within-batch duplicate-guard requirement: `addedPairsThisBatch` present and exercised by `UpsertPhotoBatch_DuplicateSharePointFileIdMatchingSameTagRule_AppliesTagOnceWithNoDuplicateKeyRisk`.
- NFR-1: no query count scales with batch size — confirmed by design (2 queries total, independent of `batchPhotoIds.Count`).
- NFR-2: no new external input — `batchPhotoIds` is derived entirely from `photosByFileId.Keys`/`tagNamesByPhoto.Keys`, already validated/owned by the batch; confirmed by reading the diff.
- No public signature of `UpsertPhotoBatchAsync` changed (still `private async Task UpsertPhotoBatchAsync(IReadOnlyList<GraphPhotoItem> batch, List<TagRule> tagRules, string? driveId, CancellationToken ct)`).

If any of these do not hold, fix inline and re-run Steps 2–3 before proceeding.

- [ ] **Step 5: Commit any formatting fixes from Step 1, if there were any**

```bash
git add -A
git commit -m "chore(photobank): apply dotnet format" || true
```

(The `|| true` is intentional — if `dotnet format` made no changes, there is nothing to commit and this step is a no-op.)

---

## Self-Review

**1. Spec coverage:**
- FR-1 (bulk-preload Rule-source tags) → Task 1 (adds the method) + Task 2 Step 3 (calls it once before the loop).
- FR-2 (bulk-preload occupied non-Rule pairs) → Task 2 Step 3 (`GetOccupiedTagPairsAsync` called once, `HashSet` lookups replace `PhotoTagExistsAsync`).
- FR-3 (preserve tag-reconciliation semantics, plus the architect-added within-batch duplicate guard) → Task 2 Step 3 (`addedPairsThisBatch`) + Step 5 regression test + the untouched `UpsertPhotoBatch_DuplicateSharePointFileIdMatchingSameTagRule_AppliesTagOnceWithNoDuplicateKeyRisk` test still passing.
- NFR-1 (O(1) queries per batch) → Task 2 Step 5 test asserts `Times.Once` for both bulk calls regardless of 3 distinct photos.
- NFR-2 (no new attack surface) → Task 3 Step 4 self-review checklist item.
- Data Model / API section (new `GetPhotoTagsByPhotosAndSourceAsync` signature, reused `GetOccupiedTagPairsAsync`) → Task 1.
- Out of Scope (no schedule/batch-size/`ReapplyRulesHandler` changes) → no task touches those; confirmed by the File Structure list above only naming the 4 files it names.

**2. Placeholder scan:** No "TBD"/"handle appropriately"/unshown code — every step above shows the exact code to write, exact commands, and exact expected output. No task says "similar to Task N" without repeating the code.

**3. Type consistency:** `PhotoTag`, `PhotoTagSource`, `int PhotoId`/`int TagId` (not `Guid`, per the actual repository code read during planning — the spec's illustrative `Guid`-based signature in its API/Interface Design section was superseded by the architect/design phases' correct `int`-based signature, which this plan follows) are used identically across Task 1's interface/implementation and Task 2's job code and test mocks. `GetPhotoTagsByPhotosAndSourceAsync` (plural, "Photos") is spelled identically everywhere it appears, distinct from the untouched singular `GetPhotoTagsByPhotoAndSourceAsync`.
