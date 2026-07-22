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
