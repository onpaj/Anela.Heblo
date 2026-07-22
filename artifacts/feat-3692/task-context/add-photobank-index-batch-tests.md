### task: add-photobank-index-batch-tests


**Files:**
- Modify: `backend/test/Anela.Heblo.Tests/Features/Photobank/PhotobankIndexJobTests.cs` (append 4 new `[Fact]` test methods)

This task assumes `backend/src/Anela.Heblo.Application/Features/Photobank/Infrastructure/Jobs/PhotobankIndexJob.cs` already contains the batched implementation described below (added by the prior `batch-photobank-index-upserts` task, which must run first). It adds 4 new tests that exercise the batching behavior itself: a multi-item single batch, a delta larger than the batch size, duplicate `SharePointFileId` within one batch, and upsert-then-delete for the same item within one delta.

#### Context: the production code these tests exercise

`PhotobankIndexJob` (`backend/src/Anela.Heblo.Application/Features/Photobank/Infrastructure/Jobs/PhotobankIndexJob.cs`) has:
- `private const int BatchSize = 200;`
- `IndexRootAsync` walks `delta.Items` once, accumulating non-deleted items into a `pendingBatch` list. It flushes (calls `UpsertPhotoBatchAsync`) when: (a) `pendingBatch.Count == BatchSize`, (b) a deleted item is encountered and `pendingBatch` is non-empty (flushed *before* the deletion is processed), or (c) the loop ends with a non-empty `pendingBatch`.
- `UpsertPhotoBatchAsync(IReadOnlyList<GraphPhotoItem> batch, List<TagRule> tagRules, string? driveId, CancellationToken ct)` has two phases, each ending in exactly one `_repo.SaveChangesAsync(ct)` call:
  - **Phase A:** for each item in `batch`, resolve the `Photo` via a batch-local `Dictionary<string, Photo>` keyed by `SharePointFileId` (checked first) falling back to `_repo.GetPhotoBySharePointFileIdAsync(item.ItemId, ct)`; create via `_repo.AddPhotoAsync` if not found; set fields; cache the resolved/created `Photo` in the dictionary keyed by `item.ItemId`. One `SaveChangesAsync` after all items in the batch are processed this way.
  - **Phase B:** compute the union of `TagRuleMatcher.GetMatchingTags(...)` names across every item in the batch; if that union is non-empty, resolve it in one call to `_repo.GetOrCreateTagsAsync(allMatchingTagNames, ct)` (returns `IReadOnlyDictionary<string, int>`) — if the union is empty, `GetOrCreateTagsAsync` is **not called at all**. Then, per item: `_repo.GetPhotoTagsByPhotoAndSourceAsync(photo.Id, PhotoTagSource.Rule, ct)` + `_repo.RemovePhotoTagsAsync(...)` (always called, regardless of whether there are any matching tag names), then for each matching tag name, `_repo.PhotoTagExistsAsync(photo.Id, tagId, ct)` guard before `_repo.AddPhotoTagAsync(...)`. One `SaveChangesAsync` after all items are processed.
- So for a root whose entire delta fits in **one** upsert batch of B non-deleted items with no interleaved deletes, `IndexRootAsync` calls `_repo.SaveChangesAsync` exactly **3** times total: 1 (Phase A) + 1 (Phase B) + 1 (root bookkeeping — `root.DeltaLink`/`root.LastIndexedAt`, unconditional, after the item loop). For a delta of N non-deleted items with no deletes, split into `ceil(N / 200)` batches, total `SaveChangesAsync` calls = `2 * ceil(N / 200) + 1`.
- `PhotobankIndexJobTests` (`backend/test/Anela.Heblo.Tests/Features/Photobank/PhotobankIndexJobTests.cs`) constructs `_job` in its constructor from `_graphServiceMock`, `_repoMock`, `_statusCheckerMock`, all `Mock<T>` fields set up in the constructor (see existing file); `_statusCheckerMock` already defaults `IsJobEnabledAsync(...) => true` for every test, so new tests do not need to touch it.

#### Step 1 — Add the 4 new test methods

Open `backend/test/Anela.Heblo.Tests/Features/Photobank/PhotobankIndexJobTests.cs`. Insert the following 4 methods immediately before the final closing brace of the `PhotobankIndexJobTests` class (i.e., directly after the closing `}` of the existing `ExecuteAsync_SkipsInactiveRoots` method, before the class's own closing `}`):

```csharp
    [Fact]
    public async Task UpsertPhotoBatch_MultipleItemsInSameBatch_FlushesSaveChangesExactlyThreeTimesTotal()
    {
        // Arrange — 2 non-deleted items, both fit in a single batch (BatchSize = 200).
        // Expected SaveChangesAsync calls: 1 (Phase A) + 1 (Phase B) + 1 (root bookkeeping) = 3.
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

        var item1 = new GraphPhotoItem
        {
            ItemId = "file-1",
            Name = "one.jpg",
            FolderPath = "Fotky/Produkty",
            WebUrl = "https://sharepoint.example.com/one.jpg",
            FileSizeBytes = 100,
            LastModifiedAt = DateTime.UtcNow,
            DriveId = "drive-1",
            IsDeleted = false,
        };
        var item2 = new GraphPhotoItem
        {
            ItemId = "file-2",
            Name = "two.jpg",
            FolderPath = "Fotky/Produkty",
            WebUrl = "https://sharepoint.example.com/two.jpg",
            FileSizeBytes = 200,
            LastModifiedAt = DateTime.UtcNow,
            DriveId = "drive-1",
            IsDeleted = false,
        };

        _repoMock
            .Setup(r => r.GetActiveRootsWithDriveAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([root]);

        _repoMock
            .Setup(r => r.GetActiveTagRulesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([tagRule]);

        _repoMock
            .Setup(r => r.GetPhotoBySharePointFileIdAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Photo?)null);

        _repoMock
            .Setup(r => r.AddPhotoAsync(It.IsAny<Photo>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _repoMock
            .Setup(r => r.GetOrCreateTagsAsync(It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, int> { ["produkty"] = 42 });

        _repoMock
            .Setup(r => r.GetPhotoTagsByPhotoAndSourceAsync(It.IsAny<int>(), PhotoTagSource.Rule, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        _repoMock
            .Setup(r => r.RemovePhotoTagsAsync(It.IsAny<IEnumerable<PhotoTag>>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _repoMock
            .Setup(r => r.PhotoTagExistsAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        _repoMock
            .Setup(r => r.AddPhotoTagAsync(It.IsAny<PhotoTag>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _repoMock
            .Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _graphServiceMock
            .Setup(g => g.GetDeltaAsync("drive-1", "root-item-1", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GraphDeltaResult
            {
                Items = [item1, item2],
                NewDeltaLink = "https://graph.microsoft.com/v1.0/drives/drive-1/items/root-item-1/delta?token=abc",
            });

        // Act
        await _job.ExecuteAsync();

        // Assert
        _repoMock.Verify(r => r.AddPhotoAsync(It.IsAny<Photo>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
        _repoMock.Verify(r => r.AddPhotoTagAsync(It.IsAny<PhotoTag>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
        _repoMock.Verify(r => r.GetOrCreateTagsAsync(It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<CancellationToken>()), Times.Once);
        _repoMock.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Exactly(3));
    }

    [Fact]
    public async Task UpsertPhotoBatch_DeltaLargerThanBatchSize_FlushesCeilNOverBatchSizeTimes()
    {
        // Arrange — 201 non-deleted items, no matching tag rules (empty active rule list),
        // so BatchSize = 200 forces 2 batches: [200 items] + [1 item].
        // Expected SaveChangesAsync calls: (1 + 1) per batch * 2 batches + 1 root bookkeeping = 5.
        var root = new PhotobankIndexRoot
        {
            Id = 1,
            SharePointPath = "/sites/test/photos",
            DriveId = "drive-1",
            RootItemId = "root-item-1",
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
        };

        const int itemCount = 201;
        var items = Enumerable.Range(0, itemCount)
            .Select(i => new GraphPhotoItem
            {
                ItemId = $"file-{i:D4}",
                Name = $"photo-{i:D4}.jpg",
                FolderPath = "Fotky/Ostatni",
                WebUrl = $"https://sharepoint.example.com/photo-{i:D4}.jpg",
                FileSizeBytes = 100,
                LastModifiedAt = DateTime.UtcNow,
                DriveId = "drive-1",
                IsDeleted = false,
            })
            .ToList();

        _repoMock
            .Setup(r => r.GetActiveRootsWithDriveAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([root]);

        _repoMock
            .Setup(r => r.GetActiveTagRulesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<TagRule>());

        _repoMock
            .Setup(r => r.GetPhotoBySharePointFileIdAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Photo?)null);

        _repoMock
            .Setup(r => r.AddPhotoAsync(It.IsAny<Photo>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _repoMock
            .Setup(r => r.GetPhotoTagsByPhotoAndSourceAsync(It.IsAny<int>(), PhotoTagSource.Rule, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        _repoMock
            .Setup(r => r.RemovePhotoTagsAsync(It.IsAny<IEnumerable<PhotoTag>>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _repoMock
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

        // Assert
        _repoMock.Verify(r => r.AddPhotoAsync(It.IsAny<Photo>(), It.IsAny<CancellationToken>()), Times.Exactly(itemCount));
        _repoMock.Verify(r => r.GetOrCreateTagsAsync(It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<CancellationToken>()), Times.Never);
        _repoMock.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Exactly(5));
    }

    [Fact]
    public async Task UpsertPhotoBatch_DuplicateSharePointFileIdWithinOneBatch_ResultsInSinglePhotoRow()
    {
        // Arrange — the same SharePointFileId appears twice as a non-deleted item in one
        // delta/batch (e.g. modified twice since last sync). The batch-local cache must
        // make both occurrences resolve to, and mutate, the same Photo instance — not
        // create two rows.
        var root = new PhotobankIndexRoot
        {
            Id = 1,
            SharePointPath = "/sites/test/photos",
            DriveId = "drive-1",
            RootItemId = "root-item-1",
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
        };

        var item1 = new GraphPhotoItem
        {
            ItemId = "file-dup",
            Name = "first-name.jpg",
            FolderPath = "Fotky/A",
            WebUrl = "https://sharepoint.example.com/first-name.jpg",
            FileSizeBytes = 111,
            LastModifiedAt = DateTime.UtcNow,
            DriveId = "drive-1",
            IsDeleted = false,
        };
        var item2 = new GraphPhotoItem
        {
            ItemId = "file-dup",
            Name = "second-name.jpg",
            FolderPath = "Fotky/B",
            WebUrl = "https://sharepoint.example.com/second-name.jpg",
            FileSizeBytes = 222,
            LastModifiedAt = DateTime.UtcNow,
            DriveId = "drive-1",
            IsDeleted = false,
        };

        _repoMock
            .Setup(r => r.GetActiveRootsWithDriveAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([root]);

        _repoMock
            .Setup(r => r.GetActiveTagRulesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<TagRule>());

        _repoMock
            .Setup(r => r.GetPhotoBySharePointFileIdAsync("file-dup", It.IsAny<CancellationToken>()))
            .ReturnsAsync((Photo?)null);

        Photo? capturedPhoto = null;
        _repoMock
            .Setup(r => r.AddPhotoAsync(It.IsAny<Photo>(), It.IsAny<CancellationToken>()))
            .Callback<Photo, CancellationToken>((p, _) => capturedPhoto = p)
            .Returns(Task.CompletedTask);

        _repoMock
            .Setup(r => r.GetPhotoTagsByPhotoAndSourceAsync(It.IsAny<int>(), PhotoTagSource.Rule, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        _repoMock
            .Setup(r => r.RemovePhotoTagsAsync(It.IsAny<IEnumerable<PhotoTag>>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _repoMock
            .Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _graphServiceMock
            .Setup(g => g.GetDeltaAsync("drive-1", "root-item-1", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GraphDeltaResult
            {
                Items = [item1, item2],
                NewDeltaLink = "https://graph.microsoft.com/v1.0/drives/drive-1/items/root-item-1/delta?token=abc",
            });

        // Act
        await _job.ExecuteAsync();

        // Assert — a single Photo row is created, and its final field values reflect the
        // second (later) item in delta order, proving both occurrences shared one instance.
        _repoMock.Verify(r => r.AddPhotoAsync(It.IsAny<Photo>(), It.IsAny<CancellationToken>()), Times.Once);
        _repoMock.Verify(r => r.GetPhotoBySharePointFileIdAsync("file-dup", It.IsAny<CancellationToken>()), Times.Once);

        capturedPhoto.Should().NotBeNull();
        capturedPhoto!.FileName.Should().Be("second-name.jpg");
        capturedPhoto.FolderPath.Should().Be("Fotky/B");
        capturedPhoto.FileSizeBytes.Should().Be(222);
    }

    [Fact]
    public async Task UpsertPhotoBatch_UpsertThenDeleteSameItemInSameDelta_PhotoEndsUpRemoved()
    {
        // Arrange — one delta containing, in order: a non-deleted item for SharePointFileId
        // "file-x", then a deleted item for the same "file-x". Per the deletion-flush rule,
        // the pending upsert batch (containing the first item) must be flushed before the
        // deletion is processed, so the deletion's lookup finds and removes the just-created
        // photo — exactly as today's per-item-flush behavior guarantees.
        var root = new PhotobankIndexRoot
        {
            Id = 1,
            SharePointPath = "/sites/test/photos",
            DriveId = "drive-1",
            RootItemId = "root-item-1",
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
        };

        var upsertItem = new GraphPhotoItem
        {
            ItemId = "file-x",
            Name = "renamed.jpg",
            FolderPath = "Fotky/Produkty",
            WebUrl = "https://sharepoint.example.com/renamed.jpg",
            FileSizeBytes = 333,
            LastModifiedAt = DateTime.UtcNow,
            DriveId = "drive-1",
            IsDeleted = false,
        };
        var deleteItem = new GraphPhotoItem
        {
            ItemId = "file-x",
            Name = string.Empty,
            FolderPath = string.Empty,
            DriveId = "drive-1",
            IsDeleted = true,
        };

        _repoMock
            .Setup(r => r.GetActiveRootsWithDriveAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([root]);

        _repoMock
            .Setup(r => r.GetActiveTagRulesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<TagRule>());

        Photo? capturedPhoto = null;
        _repoMock
            .Setup(r => r.AddPhotoAsync(It.IsAny<Photo>(), It.IsAny<CancellationToken>()))
            .Callback<Photo, CancellationToken>((p, _) => capturedPhoto = p)
            .Returns(Task.CompletedTask);

        // Lazily evaluated: returns null on the first (Phase A, upsert) lookup — before
        // AddPhotoAsync has run — and returns the just-created Photo on the second
        // (deletion) lookup, simulating that the pending batch's flush has committed it.
        _repoMock
            .Setup(r => r.GetPhotoBySharePointFileIdAsync("file-x", It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => capturedPhoto);

        _repoMock
            .Setup(r => r.GetPhotoTagsByPhotoAndSourceAsync(It.IsAny<int>(), PhotoTagSource.Rule, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        _repoMock
            .Setup(r => r.RemovePhotoTagsAsync(It.IsAny<IEnumerable<PhotoTag>>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _repoMock
            .Setup(r => r.RemovePhotoAsync(It.IsAny<Photo>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _repoMock
            .Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _graphServiceMock
            .Setup(g => g.GetDeltaAsync("drive-1", "root-item-1", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GraphDeltaResult
            {
                Items = [upsertItem, deleteItem],
                NewDeltaLink = "https://graph.microsoft.com/v1.0/drives/drive-1/items/root-item-1/delta?token=abc",
            });

        // Act
        await _job.ExecuteAsync();

        // Assert — the photo was created, then removed: no orphaned row.
        capturedPhoto.Should().NotBeNull();
        _repoMock.Verify(r => r.AddPhotoAsync(It.IsAny<Photo>(), It.IsAny<CancellationToken>()), Times.Once);
        _repoMock.Verify(r => r.RemovePhotoAsync(capturedPhoto!, It.IsAny<CancellationToken>()), Times.Once);
    }
```

Ensure the file's opening `using` list already includes everything these tests need: `Anela.Heblo.Application.Features.Photobank.Infrastructure.Jobs`, `Anela.Heblo.Application.Features.Photobank.Services`, `Anela.Heblo.Domain.Features.BackgroundJobs`, `Anela.Heblo.Domain.Features.Photobank`, `FluentAssertions`, `Microsoft.Extensions.Logging.Abstractions`, `Moq` — these are already present at the top of the existing file and require no changes. `Enumerable.Range(...).Select(...).ToList()` in the second new test relies on `System.Linq`, which is available via `ImplicitUsings` (already enabled for this test project — no new `using` needed).

#### Step 2 — Run the new tests

```bash
cd /home/user/worktrees/feature-3692-Arch-Review-Photobank-Photobankindexjob-Calls-Save
dotnet test --filter "FullyQualifiedName~PhotobankIndexJobTests"
```

Expected output: `Passed! - Failed: 0, Passed: 9, Skipped: 0` (5 pre-existing + 4 new).

If `UpsertPhotoBatch_DeltaLargerThanBatchSize_FlushesCeilNOverBatchSizeTimes` fails on the `SaveChangesAsync` count assertion, double check the item count constant (`itemCount = 201`) against `BatchSize = 200` in production code — the test assumes exactly 2 batches (200 + 1); if `BatchSize` in production code is ever changed, this test's expected `Times.Exactly(5)` must change to match `2 * ceil(itemCount / BatchSize) + 1`.

If `UpsertPhotoBatch_UpsertThenDeleteSameItemInSameDelta_PhotoEndsUpRemoved` fails with a null-reference on `capturedPhoto`, check that `AddPhotoAsync`'s `Callback` runs before the second `GetPhotoBySharePointFileIdAsync` invocation — this requires Phase A's flush (`SaveChangesAsync`) to complete before `IndexRootAsync` processes the deleted item, i.e. that the "flush pending batch before delete" ordering in `IndexRootAsync` was implemented correctly in the prior task.

#### Step 3 — Build and full regression run

```bash
cd /home/user/worktrees/feature-3692-Arch-Review-Photobank-Photobankindexjob-Calls-Save
dotnet build
dotnet test
```

Expected: `dotnet build` → `Build succeeded.`; `dotnet test` → all tests in the solution pass (no regressions introduced outside `PhotobankIndexJobTests`, since no other production file was touched by this plan).

#### Step 4 — Format check

```bash
cd /home/user/worktrees/feature-3692-Arch-Review-Photobank-Photobankindexjob-Calls-Save
dotnet format --verify-no-changes
```

If this reports formatting issues in the touched test file, run `dotnet format` (no `--verify-no-changes`) to auto-fix, then re-run `dotnet build` and the filtered test command from Step 2 to confirm nothing broke.

#### Step 5 — Commit

```bash
cd /home/user/worktrees/feature-3692-Arch-Review-Photobank-Photobankindexjob-Calls-Save
git add backend/test/Anela.Heblo.Tests/Features/Photobank/PhotobankIndexJobTests.cs
git commit -m "Add batching test coverage for PhotobankIndexJob upsert path"
```

---

## Self-review against the spec

- **FR-1 (batch the photo-entity flush):** `batch-photobank-index-upserts` Step 3, Phase A of `UpsertPhotoBatchAsync` — single `SaveChangesAsync` after all items processed; field semantics and `pathChanged`/`LastAutoTaggedAt` reset preserved verbatim. Covered by `UpsertPhotoBatch_MultipleItemsInSameBatch_FlushesSaveChangesExactlyThreeTimesTotal` (`add-photobank-index-batch-tests`).
- **FR-2 (batch the tag-rule flush, amended to bulk `GetOrCreateTagsAsync`):** Phase B, single flush; bulk tag resolution via `GetOrCreateTagsAsync` per the arch-review's Decision 1 and Specification Amendment 1. Existing duplicate guard (`PhotoTagExistsAsync`) preserved. Covered by the same test plus the two updated pre-existing tests.
- **FR-3 (chunk into fixed-size batches, `BatchSize = 200` constant, order preserved, deleted items processed inline):** `IndexRootAsync`'s accumulate/flush loop in Step 3. Covered by `UpsertPhotoBatch_DeltaLargerThanBatchSize_FlushesCeilNOverBatchSizeTimes`.
- **FR-3 amendment / arch-review Decision 2 (flush pending batch before a delete):** implemented in the `item.IsDeleted` branch of `IndexRootAsync`. Covered by `UpsertPhotoBatch_UpsertThenDeleteSameItemInSameDelta_PhotoEndsUpRemoved`.
- **FR-4 (preserve root bookkeeping / try-catch):** untouched in Step 3 — `root.DeltaLink`/`root.LastIndexedAt`/single flush/`try`-`catch` block are copied over unchanged from the original file. Covered indirectly by all tests (every test's `_repoMock.Verify(SaveChangesAsync, ...)` count includes the +1 root-bookkeeping flush) and directly by the pre-existing `ExecuteAsync_PersistsDeltaLink_AfterRun`.
- **FR-5 / arch-review Decision 3 (batch-local `Dictionary<string, Photo>` cache):** implemented in Phase A of `UpsertPhotoBatchAsync`. Covered by `UpsertPhotoBatch_DuplicateSharePointFileIdWithinOneBatch_ResultsInSinglePhotoRow`.
- **NFR-1 (performance / round-trip math):** addressed by the batching structure itself; the `2 * ceil(N/BatchSize) + 1` (or more, if deletes interleave) accounting is spelled out in `add-photobank-index-batch-tests`' context section and validated by the two round-trip-counting tests.
- **NFR-2 (idempotency / uniqueness under batching):** the batch-local cache (FR-5) and the unchanged `PhotoTagExistsAsync` guard are exactly the two mechanisms NFR-2 requires; both are implemented and tested.
- **Out of Scope items** (read-side batching, configurable batch size, parallelization, cron/flag/logging-format changes, `ReapplyRulesHandler`/`RetagPhotosHandler` changes, schema changes): none of these are touched anywhere in this plan.

No placeholders, no references to undefined types/methods — every method signature used (`UpsertPhotoBatchAsync`, all `IPhotobankRepository` members) is either quoted from the actual current interface/implementation (read from the repo) or defined in full within this plan's own code blocks. Method names and signatures are consistent between the "Shared context" section, the `batch-photobank-index-upserts` task, and the `add-photobank-index-batch-tests` task.
