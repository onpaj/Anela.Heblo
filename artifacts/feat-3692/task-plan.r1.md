# Batch `SaveChangesAsync` calls in `PhotobankIndexJob` — Implementation Plan

**Goal:** Replace `PhotobankIndexJob`'s per-item `SaveChangesAsync` pair in the delta-upsert path with a batched (default 200 items) accumulate/flush loop, cutting DB round-trips from `2N` to `at most 2 * ceil(N / 200)`, with all upsert and tag-rule behavior preserved exactly.

**Architecture:** All changes are confined to one file, `backend/src/Anela.Heblo.Application/Features/Photobank/Infrastructure/Jobs/PhotobankIndexJob.cs`: the single-item `UpsertPhotoAsync` is replaced by a batch-oriented `UpsertPhotoBatchAsync(IReadOnlyList<GraphPhotoItem> batch, List<TagRule> tagRules, string? driveId, CancellationToken ct)`, and `IndexRootAsync`'s item loop becomes a single-pass accumulate-into-`pendingBatch` / flush-on-`BatchSize`-or-on-delete loop. Tag-name resolution moves from per-item `GetOrCreateTagAsync` to one bulk `GetOrCreateTagsAsync` call per batch (mirroring the existing `ReapplyRulesHandler` precedent), and a batch-local `Dictionary<string, Photo>` cache prevents duplicate `Photo` rows when the same `SharePointFileId` appears twice in one batch. No `IPhotobankRepository`/`PhotobankRepository` interface changes; no schema changes; no new config surface.

**Tech Stack:** .NET 8, C# (nullable, `ImplicitUsings` enabled), MediatR-free plain background job (`IRecurringJob`), EF Core (`ApplicationDbContext` via `IPhotobankRepository`), xUnit + Moq + FluentAssertions for tests.

---

## Shared context (applies to both tasks below)

**Repository root:** `/home/user/worktrees/feature-3692-Arch-Review-Photobank-Photobankindexjob-Calls-Save` (this plan assumes commands are run from this directory; the solution file `Anela.Heblo.sln` lives at this root).

**File being changed:**
`backend/src/Anela.Heblo.Application/Features/Photobank/Infrastructure/Jobs/PhotobankIndexJob.cs`

**Test file being changed:**
`backend/test/Anela.Heblo.Tests/Features/Photobank/PhotobankIndexJobTests.cs`

**Relevant `IPhotobankRepository` members** (interface at `backend/src/Anela.Heblo.Domain/Features/Photobank/IPhotobankRepository.cs`, implementation at `backend/src/Anela.Heblo.Persistence/Photobank/PhotobankRepository.cs`) — unchanged by this plan, used as-is:
```csharp
Task<Photo?> GetPhotoBySharePointFileIdAsync(string sharePointFileId, CancellationToken cancellationToken);
Task AddPhotoAsync(Photo photo, CancellationToken cancellationToken);
Task RemovePhotoAsync(Photo photo, CancellationToken cancellationToken);
Task<IReadOnlyDictionary<string, int>> GetOrCreateTagsAsync(IReadOnlyCollection<string> normalizedNames, CancellationToken cancellationToken);
Task AddPhotoTagAsync(PhotoTag photoTag, CancellationToken cancellationToken);
Task<bool> PhotoTagExistsAsync(int photoId, int tagId, CancellationToken cancellationToken);
Task<List<PhotoTag>> GetPhotoTagsByPhotoAndSourceAsync(int photoId, PhotoTagSource source, CancellationToken cancellationToken);
Task RemovePhotoTagsAsync(IEnumerable<PhotoTag> photoTags, CancellationToken cancellationToken);
Task<List<TagRule>> GetActiveTagRulesAsync(CancellationToken cancellationToken);
Task<List<PhotobankIndexRoot>> GetActiveRootsWithDriveAsync(CancellationToken cancellationToken);
Task SaveChangesAsync(CancellationToken cancellationToken);
```
`GetOrCreateTagAsync(string, CancellationToken)` (singular) still exists on the interface and is used elsewhere (e.g. by other Photobank use cases) — it is **not removed**, simply no longer called from `PhotobankIndexJob`. Its implementation has a hidden internal `SaveChangesAsync` when it creates a brand-new tag; `GetOrCreateTagsAsync` (bulk) also has an internal flush when it creates new tags, but only ever at most once per call regardless of how many new tags are created — this is why the batch code must call the bulk method once per batch, not the singular method per item.

`GraphPhotoItem` (from `backend/src/Anela.Heblo.Application/Features/Photobank/Services/IPhotobankGraphService.cs`) is a plain mutable class with `ItemId`, `Name`, `FolderPath`, `WebUrl`, `FileSizeBytes`, `LastModifiedAt`, `DriveId`, `IsDeleted` — no custom equality, so it must not be used as a dictionary key (reference equality would be misleading); use parallel lists/tuples keyed by `ItemId` (a string) instead.

`TagRuleMatcher.GetMatchingTags(string folderPath, string fileName, IEnumerable<TagRule> rules)` returns `IReadOnlyList<string>` — unchanged, from `backend/src/Anela.Heblo.Domain/Features/Photobank/TagRuleMatcher.cs`.

**Current (pre-change) production code** for reference — this is exactly what task 1 replaces:
```csharp
using Anela.Heblo.Application.Features.Photobank.Services;
using Anela.Heblo.Domain.Features.BackgroundJobs;
using Anela.Heblo.Domain.Features.Photobank;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.Photobank.Infrastructure.Jobs;

public class PhotobankIndexJob : IRecurringJob
{
    private readonly IPhotobankGraphService _graphService;
    private readonly IPhotobankRepository _repo;
    private readonly IRecurringJobStatusChecker _statusChecker;
    private readonly ILogger<PhotobankIndexJob> _logger;

    public RecurringJobMetadata Metadata { get; } = new()
    {
        JobName = "photobank-index",
        DisplayName = "Photobank Index",
        Description = "Syncs SharePoint photos into the Photobank via Graph delta API",
        CronExpression = "0 3 * * *",
        DefaultIsEnabled = true,
    };

    public PhotobankIndexJob(
        IPhotobankGraphService graphService,
        IPhotobankRepository repo,
        IRecurringJobStatusChecker statusChecker,
        ILogger<PhotobankIndexJob> logger)
    {
        _graphService = graphService;
        _repo = repo;
        _statusChecker = statusChecker;
        _logger = logger;
    }

    public async Task ExecuteAsync(CancellationToken cancellationToken = default)
    {
        if (!await _statusChecker.IsJobEnabledAsync(Metadata.JobName, cancellationToken))
        {
            _logger.LogInformation("Job {JobName} is disabled. Skipping.", Metadata.JobName);
            return;
        }

        var roots = await _repo.GetActiveRootsWithDriveAsync(cancellationToken);

        _logger.LogInformation("Starting {JobName} — {Count} active roots", Metadata.JobName, roots.Count);

        foreach (var root in roots)
        {
            await IndexRootAsync(root, cancellationToken);
        }
    }

    private async Task IndexRootAsync(PhotobankIndexRoot root, CancellationToken ct)
    {
        _logger.LogInformation(
            "Indexing root {RootId} (DriveId={DriveId}, RootItemId={RootItemId})",
            root.Id,
            root.DriveId,
            root.RootItemId);

        try
        {
            if (string.IsNullOrEmpty(root.RootItemId))
            {
                _logger.LogInformation("Resolving item ID for path {Path} in drive {DriveId}", root.SharePointPath, root.DriveId);
                root.RootItemId = await _graphService.ResolveItemIdAsync(root.DriveId!, root.SharePointPath!, ct);
                await _repo.SaveChangesAsync(ct);
            }

            var delta = await _graphService.GetDeltaAsync(root.DriveId!, root.RootItemId!, root.DeltaLink, ct);

            var activeTagRules = await _repo.GetActiveTagRulesAsync(ct);

            int upserted = 0, deleted = 0;

            foreach (var item in delta.Items)
            {
                if (item.IsDeleted)
                {
                    var existing = await _repo.GetPhotoBySharePointFileIdAsync(item.ItemId, ct);
                    if (existing != null)
                    {
                        await _repo.RemovePhotoAsync(existing, ct);
                        deleted++;
                    }
                }
                else
                {
                    await UpsertPhotoAsync(item, activeTagRules, root.DriveId, ct);
                    upserted++;
                }
            }

            root.DeltaLink = delta.NewDeltaLink;
            root.LastIndexedAt = DateTime.UtcNow;
            await _repo.SaveChangesAsync(ct);

            _logger.LogInformation(
                "Root {RootId}: upserted={Upserted}, deleted={Deleted}",
                root.Id,
                upserted,
                deleted);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to index root {RootId}", root.Id);
        }
    }

    private async Task UpsertPhotoAsync(GraphPhotoItem item, List<TagRule> tagRules, string? driveId, CancellationToken ct)
    {
        var photo = await _repo.GetPhotoBySharePointFileIdAsync(item.ItemId, ct);

        var pathChanged = photo != null &&
            (photo.FolderPath != item.FolderPath || photo.FileName != item.Name);

        if (photo == null)
        {
            photo = new Photo
            {
                SharePointFileId = item.ItemId,
                IndexedAt = DateTime.UtcNow,
            };
            await _repo.AddPhotoAsync(photo, ct);
        }

        photo.FileName = item.Name;
        photo.FolderPath = item.FolderPath;
        photo.SharePointWebUrl = item.WebUrl;
        photo.FileSizeBytes = item.FileSizeBytes;
        photo.ModifiedAt = item.LastModifiedAt ?? DateTime.UtcNow;
        photo.DriveId = driveId;

        if (pathChanged)
            photo.LastAutoTaggedAt = null;

        await _repo.SaveChangesAsync(ct);

        // Re-apply rule tags: remove existing Rule-source tags, add new ones
        var existingRuleTags = await _repo.GetPhotoTagsByPhotoAndSourceAsync(photo.Id, PhotoTagSource.Rule, ct);
        await _repo.RemovePhotoTagsAsync(existingRuleTags, ct);

        var matchingTagNames = TagRuleMatcher.GetMatchingTags(item.FolderPath, item.Name, tagRules);
        foreach (var tagName in matchingTagNames)
        {
            var tag = await _repo.GetOrCreateTagAsync(tagName, ct);
            if (await _repo.PhotoTagExistsAsync(photo.Id, tag!.Id, ct)) continue;

            await _repo.AddPhotoTagAsync(new PhotoTag
            {
                PhotoId = photo.Id,
                TagId = tag!.Id,
                Source = PhotoTagSource.Rule,
                CreatedAt = DateTime.UtcNow,
            }, ct);
        }

        await _repo.SaveChangesAsync(ct);
    }
}
```

---

### task: batch-photobank-index-upserts

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/Photobank/Infrastructure/Jobs/PhotobankIndexJob.cs` (full rewrite)
- Modify: `backend/test/Anela.Heblo.Tests/Features/Photobank/PhotobankIndexJobTests.cs` (update 2 existing test methods' mocks only — `ExecuteAsync_InsertsNewPhoto_WithRuleTagsApplied` and `UpsertPhoto_WhenTagAlreadyExists_SkipsInsert`)

This task replaces the per-item `SaveChangesAsync` pair in `PhotobankIndexJob` with a batched accumulate/flush loop (default batch size 200), and updates the two existing tests whose mocks reference the now-unused `GetOrCreateTagAsync(string, ...)` so the suite compiles and passes against the new code. New test cases exercising the batching behavior itself are added in the next task (`add-photobank-index-batch-tests`), which depends on this task's production code already being in place.

#### Step 1 — Confirm current test baseline passes

Run the existing Photobank index job tests against the current (pre-change) code to confirm the starting point is green:

```bash
cd /home/user/worktrees/feature-3692-Arch-Review-Photobank-Photobankindexjob-Calls-Save
dotnet test --filter "FullyQualifiedName~PhotobankIndexJobTests"
```

Expected output: `Passed! - Failed: 0, Passed: 5, Skipped: 0` (5 existing test methods: `ExecuteAsync_InsertsNewPhoto_WithRuleTagsApplied`, `UpsertPhoto_WhenTagAlreadyExists_SkipsInsert`, `ExecuteAsync_RemovesPhoto_WhenDeleted`, `ExecuteAsync_PersistsDeltaLink_AfterRun`, `ExecuteAsync_SkipsInactiveRoots`).

#### Step 2 — Update the two existing tests' mocks to the bulk tag-resolution method

These two tests currently mock the singular `GetOrCreateTagAsync(string, CancellationToken)`, which the new production code will no longer call (Step 3 replaces it with `GetOrCreateTagsAsync(IReadOnlyCollection<string>, CancellationToken)`, per-batch). Update them now so that after Step 3 the whole suite is green in one pass.

In `backend/test/Anela.Heblo.Tests/Features/Photobank/PhotobankIndexJobTests.cs`, in `ExecuteAsync_InsertsNewPhoto_WithRuleTagsApplied`, replace:
```csharp
        var tag = new Tag { Id = 42, Name = "produkty" };
        _repoMock
            .Setup(r => r.GetOrCreateTagAsync("produkty", It.IsAny<CancellationToken>()))
            .ReturnsAsync(tag);
```
with:
```csharp
        _repoMock
            .Setup(r => r.GetOrCreateTagsAsync(It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, int> { ["produkty"] = 42 });
```

In the same test method, the assertions below still reference `capturedPhotoTag.TagId.Should().Be(42);` — leave this assertion unchanged; it still holds because the production code will resolve `"produkty"` to id `42` via the dictionary returned by `GetOrCreateTagsAsync`.

In `UpsertPhoto_WhenTagAlreadyExists_SkipsInsert`, replace:
```csharp
        var tag = new Tag { Id = 42, Name = "produkty" };
        _repoMock
            .Setup(r => r.GetOrCreateTagAsync("produkty", It.IsAny<CancellationToken>()))
            .ReturnsAsync(tag);
```
with:
```csharp
        _repoMock
            .Setup(r => r.GetOrCreateTagsAsync(It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, int> { ["produkty"] = 42 });
```

Do not change anything else in either test method (the `PhotoTagExistsAsync` mocks, `Times.Once`/`Times.Never` assertions, and all other setup stay exactly as they are).

Do **not** run the tests yet — against the still-unchanged production code, these two tests will now fail (production code still calls `GetOrCreateTagAsync`, which is no longer mocked and will return Moq's default `null`, causing a `NullReferenceException` at `tag!.Id`). This is expected; proceed directly to Step 3.

#### Step 3 — Replace `PhotobankIndexJob.cs` with the batched implementation

Overwrite the full contents of `backend/src/Anela.Heblo.Application/Features/Photobank/Infrastructure/Jobs/PhotobankIndexJob.cs` with:

```csharp
using Anela.Heblo.Application.Features.Photobank.Services;
using Anela.Heblo.Domain.Features.BackgroundJobs;
using Anela.Heblo.Domain.Features.Photobank;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.Photobank.Infrastructure.Jobs;

public class PhotobankIndexJob : IRecurringJob
{
    private const int BatchSize = 200;

    private readonly IPhotobankGraphService _graphService;
    private readonly IPhotobankRepository _repo;
    private readonly IRecurringJobStatusChecker _statusChecker;
    private readonly ILogger<PhotobankIndexJob> _logger;

    public RecurringJobMetadata Metadata { get; } = new()
    {
        JobName = "photobank-index",
        DisplayName = "Photobank Index",
        Description = "Syncs SharePoint photos into the Photobank via Graph delta API",
        CronExpression = "0 3 * * *",
        DefaultIsEnabled = true,
    };

    public PhotobankIndexJob(
        IPhotobankGraphService graphService,
        IPhotobankRepository repo,
        IRecurringJobStatusChecker statusChecker,
        ILogger<PhotobankIndexJob> logger)
    {
        _graphService = graphService;
        _repo = repo;
        _statusChecker = statusChecker;
        _logger = logger;
    }

    public async Task ExecuteAsync(CancellationToken cancellationToken = default)
    {
        if (!await _statusChecker.IsJobEnabledAsync(Metadata.JobName, cancellationToken))
        {
            _logger.LogInformation("Job {JobName} is disabled. Skipping.", Metadata.JobName);
            return;
        }

        var roots = await _repo.GetActiveRootsWithDriveAsync(cancellationToken);

        _logger.LogInformation("Starting {JobName} — {Count} active roots", Metadata.JobName, roots.Count);

        foreach (var root in roots)
        {
            await IndexRootAsync(root, cancellationToken);
        }
    }

    private async Task IndexRootAsync(PhotobankIndexRoot root, CancellationToken ct)
    {
        _logger.LogInformation(
            "Indexing root {RootId} (DriveId={DriveId}, RootItemId={RootItemId})",
            root.Id,
            root.DriveId,
            root.RootItemId);

        try
        {
            if (string.IsNullOrEmpty(root.RootItemId))
            {
                _logger.LogInformation("Resolving item ID for path {Path} in drive {DriveId}", root.SharePointPath, root.DriveId);
                root.RootItemId = await _graphService.ResolveItemIdAsync(root.DriveId!, root.SharePointPath!, ct);
                await _repo.SaveChangesAsync(ct);
            }

            var delta = await _graphService.GetDeltaAsync(root.DriveId!, root.RootItemId!, root.DeltaLink, ct);

            var activeTagRules = await _repo.GetActiveTagRulesAsync(ct);

            int upserted = 0, deleted = 0;
            var pendingBatch = new List<GraphPhotoItem>();

            foreach (var item in delta.Items)
            {
                if (item.IsDeleted)
                {
                    // Flush any pending upsert batch first, so this delete's lookup
                    // always sees every prior item in the delta as committed — matching
                    // today's per-item-flush guarantee and preventing orphaned rows when
                    // an item is upserted then deleted within the same delta.
                    if (pendingBatch.Count > 0)
                    {
                        await UpsertPhotoBatchAsync(pendingBatch, activeTagRules, root.DriveId, ct);
                        upserted += pendingBatch.Count;
                        pendingBatch.Clear();
                    }

                    var existing = await _repo.GetPhotoBySharePointFileIdAsync(item.ItemId, ct);
                    if (existing != null)
                    {
                        await _repo.RemovePhotoAsync(existing, ct);
                        deleted++;
                    }
                }
                else
                {
                    pendingBatch.Add(item);
                    if (pendingBatch.Count == BatchSize)
                    {
                        await UpsertPhotoBatchAsync(pendingBatch, activeTagRules, root.DriveId, ct);
                        upserted += pendingBatch.Count;
                        pendingBatch.Clear();
                    }
                }
            }

            if (pendingBatch.Count > 0)
            {
                await UpsertPhotoBatchAsync(pendingBatch, activeTagRules, root.DriveId, ct);
                upserted += pendingBatch.Count;
                pendingBatch.Clear();
            }

            root.DeltaLink = delta.NewDeltaLink;
            root.LastIndexedAt = DateTime.UtcNow;
            await _repo.SaveChangesAsync(ct);

            _logger.LogInformation(
                "Root {RootId}: upserted={Upserted}, deleted={Deleted}",
                root.Id,
                upserted,
                deleted);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to index root {RootId}", root.Id);
        }
    }

    private async Task UpsertPhotoBatchAsync(IReadOnlyList<GraphPhotoItem> batch, List<TagRule> tagRules, string? driveId, CancellationToken ct)
    {
        // Phase A: upsert Photo entities for the whole batch, single flush.
        // A batch-local cache (keyed by SharePointFileId) guards against duplicate rows
        // when the same SharePointFileId appears twice as a non-deleted item in one batch —
        // both occurrences resolve to, and mutate, the same tracked Photo instance.
        var photosByFileId = new Dictionary<string, Photo>();

        foreach (var item in batch)
        {
            if (!photosByFileId.TryGetValue(item.ItemId, out var photo))
            {
                photo = await _repo.GetPhotoBySharePointFileIdAsync(item.ItemId, ct);
            }

            var pathChanged = photo != null &&
                (photo.FolderPath != item.FolderPath || photo.FileName != item.Name);

            if (photo == null)
            {
                photo = new Photo
                {
                    SharePointFileId = item.ItemId,
                    IndexedAt = DateTime.UtcNow,
                };
                await _repo.AddPhotoAsync(photo, ct);
            }

            photo.FileName = item.Name;
            photo.FolderPath = item.FolderPath;
            photo.SharePointWebUrl = item.WebUrl;
            photo.FileSizeBytes = item.FileSizeBytes;
            photo.ModifiedAt = item.LastModifiedAt ?? DateTime.UtcNow;
            photo.DriveId = driveId;

            if (pathChanged)
                photo.LastAutoTaggedAt = null;

            photosByFileId[item.ItemId] = photo;
        }

        await _repo.SaveChangesAsync(ct);

        // Phase B: re-apply rule tags for the whole batch, single flush.
        // Tag names are pre-resolved once for the whole batch via the bulk
        // GetOrCreateTagsAsync, not per item via the singular GetOrCreateTagAsync —
        // the singular method has a hidden internal SaveChangesAsync on new-tag
        // creation that would defeat the round-trip reduction (mirrors ReapplyRulesHandler).
        var itemMatches = new List<(GraphPhotoItem Item, IReadOnlyList<string> TagNames)>(batch.Count);
        var allMatchingTagNames = new HashSet<string>();

        foreach (var item in batch)
        {
            var matches = TagRuleMatcher.GetMatchingTags(item.FolderPath, item.Name, tagRules);
            itemMatches.Add((item, matches));
            foreach (var name in matches)
                allMatchingTagNames.Add(name);
        }

        var tagIdsByName = allMatchingTagNames.Count > 0
            ? await _repo.GetOrCreateTagsAsync(allMatchingTagNames, ct)
            : new Dictionary<string, int>();

        foreach (var (item, tagNames) in itemMatches)
        {
            var photo = photosByFileId[item.ItemId];

            // Re-apply rule tags: remove existing Rule-source tags, add new ones
            var existingRuleTags = await _repo.GetPhotoTagsByPhotoAndSourceAsync(photo.Id, PhotoTagSource.Rule, ct);
            await _repo.RemovePhotoTagsAsync(existingRuleTags, ct);

            foreach (var tagName in tagNames)
            {
                if (!tagIdsByName.TryGetValue(tagName, out var tagId)) continue;
                if (await _repo.PhotoTagExistsAsync(photo.Id, tagId, ct)) continue;

                await _repo.AddPhotoTagAsync(new PhotoTag
                {
                    PhotoId = photo.Id,
                    TagId = tagId,
                    Source = PhotoTagSource.Rule,
                    CreatedAt = DateTime.UtcNow,
                }, ct);
            }
        }

        await _repo.SaveChangesAsync(ct);
    }
}
```

Key points carried over unchanged from the original: field-by-field upsert semantics (`FileName`, `FolderPath`, `SharePointWebUrl`, `FileSizeBytes`, `ModifiedAt`, `DriveId`, `SharePointFileId`/`IndexedAt` on create), `pathChanged` → `LastAutoTaggedAt = null` reset, the `PhotoTagExistsAsync` duplicate guard before adding a `PhotoTag`, deletion handling (`GetPhotoBySharePointFileIdAsync` + `RemovePhotoAsync`, no flush — covered by the end-of-root `SaveChangesAsync`), root bookkeeping (`root.DeltaLink`/`root.LastIndexedAt`, single flush after the loop), and the outer `try`/`catch` (catch-log-continue, no rethrow).

#### Step 4 — Build and run the full suite

```bash
cd /home/user/worktrees/feature-3692-Arch-Review-Photobank-Photobankindexjob-Calls-Save
dotnet build
```
Expected output: `Build succeeded.` with 0 errors.

```bash
dotnet test --filter "FullyQualifiedName~PhotobankIndexJobTests"
```
Expected output: `Passed! - Failed: 0, Passed: 5, Skipped: 0`.

If any of the 5 tests fail, check first whether the failure is in `ExecuteAsync_InsertsNewPhoto_WithRuleTagsApplied` or `UpsertPhoto_WhenTagAlreadyExists_SkipsInsert` — these are the two most likely to regress if the `GetOrCreateTagsAsync` mock from Step 2 doesn't exactly match production code's call (e.g. mismatched `IReadOnlyCollection<string>` matcher).

#### Step 5 — Format and full build check

```bash
cd /home/user/worktrees/feature-3692-Arch-Review-Photobank-Photobankindexjob-Calls-Save
dotnet format
dotnet build
```
Expected: `dotnet format` reports no remaining issues (or auto-fixes whitespace-only diffs in the two files touched); `dotnet build` succeeds with 0 errors, 0 warnings introduced by this change.

#### Step 6 — Commit

```bash
cd /home/user/worktrees/feature-3692-Arch-Review-Photobank-Photobankindexjob-Calls-Save
git add backend/src/Anela.Heblo.Application/Features/Photobank/Infrastructure/Jobs/PhotobankIndexJob.cs backend/test/Anela.Heblo.Tests/Features/Photobank/PhotobankIndexJobTests.cs
git commit -m "Batch SaveChangesAsync calls in PhotobankIndexJob upsert path"
```

---

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
