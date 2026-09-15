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
