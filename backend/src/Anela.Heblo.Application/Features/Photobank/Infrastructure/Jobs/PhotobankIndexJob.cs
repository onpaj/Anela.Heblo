using Anela.Heblo.Application.Features.Photobank.Services;
using Anela.Heblo.Domain.Features.BackgroundJobs;
using Anela.Heblo.Domain.Features.Photobank;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.Photobank.Infrastructure.Jobs;

public class PhotobankIndexJob : IRecurringJob
{
    private const int BatchSize = 200;

    private readonly IPhotobankGraphService _graphService;
    private readonly IPhotobankRootRepository _rootRepository;
    private readonly IPhotobankTagRuleRepository _tagRuleRepository;
    private readonly IPhotobankPhotoRepository _photoRepository;
    private readonly IPhotobankTagRepository _tagRepository;
    private readonly IPhotobankPhotoTagRepository _photoTagRepository;
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
        IPhotobankRootRepository rootRepository,
        IPhotobankTagRuleRepository tagRuleRepository,
        IPhotobankPhotoRepository photoRepository,
        IPhotobankTagRepository tagRepository,
        IPhotobankPhotoTagRepository photoTagRepository,
        IRecurringJobStatusChecker statusChecker,
        ILogger<PhotobankIndexJob> logger)
    {
        _graphService = graphService;
        _rootRepository = rootRepository;
        _tagRuleRepository = tagRuleRepository;
        _photoRepository = photoRepository;
        _tagRepository = tagRepository;
        _photoTagRepository = photoTagRepository;
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

        var roots = await _rootRepository.GetActiveRootsWithDriveAsync(cancellationToken);

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
                await _rootRepository.SaveChangesAsync(ct);
            }

            var delta = await _graphService.GetDeltaAsync(root.DriveId!, root.RootItemId!, root.DeltaLink, ct);

            var activeTagRules = await _tagRuleRepository.GetActiveTagRulesAsync(ct);

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

                    var existing = await _photoRepository.GetPhotoBySharePointFileIdAsync(item.ItemId, ct);
                    if (existing != null)
                    {
                        await _photoRepository.RemovePhotoAsync(existing, ct);
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
            await _rootRepository.SaveChangesAsync(ct);

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
                photo = await _photoRepository.GetPhotoBySharePointFileIdAsync(item.ItemId, ct);
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
                await _photoRepository.AddPhotoAsync(photo, ct);
            }

            photo.FileName = item.Name;
            photo.FolderPath = item.FolderPath;
            photo.SharePointWebUrl = item.WebUrl;
            photo.FileSizeBytes = item.FileSizeBytes;
            // NOTE: this only fixes the in-memory Kind of the Graph-sourced value (relevant to
            // anything that reads `photo.ModifiedAt` before SaveChangesAsync, e.g. API responses
            // serialized within this request). It does NOT change what reaches Npgsql:
            // ApplicationDbContext.OnModelCreating installs a global DateTime value converter that
            // unconditionally re-stamps every DateTime/DateTime? property to Kind=Unspecified right
            // before every write, regardless of the Kind assigned here. If the recurring
            // "Cannot write DateTime with Kind=Unspecified to ... 'timestamp with time zone'"
            // exception is still occurring, the cause is schema drift on the physical column (see
            // PhotobankSchemaHealthCheck / docs/development/setup.md "Photobank column-type drift"),
            // not an application-layer Kind bug — this line does not remediate that.
            photo.ModifiedAt = item.LastModifiedAt.HasValue
                ? DateTime.SpecifyKind(item.LastModifiedAt.Value, DateTimeKind.Utc)
                : DateTime.UtcNow;
            photo.DriveId = driveId;

            if (pathChanged)
                photo.LastAutoTaggedAt = null;

            photosByFileId[item.ItemId] = photo;
        }

        await _photoRepository.SaveChangesAsync(ct);

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
            ? await _tagRepository.GetOrCreateTagsAsync(allMatchingTagNames, ct)
            : new Dictionary<string, int>();

        // Dedupe by Photo before applying tags: when the same SharePointFileId appears
        // twice as a non-deleted item in one batch, both occurrences share the same
        // tracked Photo instance (see photosByFileId above), but the tag removal/add
        // loop below reads existing tags via DB queries that can't see this batch's own
        // unflushed changes yet. Processing the same photo twice in that loop would
        // either leave stale tags behind (different rules matched) or double-insert the
        // same (PhotoId, TagId) pair and crash SaveChangesAsync (same rule matched
        // twice). Collapsing to one entry per Photo — keeping the last item's matches,
        // mirroring Phase A's last-write-wins collapse — avoids both failure modes.
        var tagNamesByPhoto = new Dictionary<Photo, IReadOnlyList<string>>();
        foreach (var (item, tagNames) in itemMatches)
        {
            var photo = photosByFileId[item.ItemId];
            tagNamesByPhoto[photo] = tagNames;
        }

        foreach (var (photo, tagNames) in tagNamesByPhoto)
        {
            // Re-apply rule tags: remove existing Rule-source tags, add new ones
            var existingRuleTags = await _photoTagRepository.GetPhotoTagsByPhotoAndSourceAsync(photo.Id, PhotoTagSource.Rule, ct);
            await _photoTagRepository.RemovePhotoTagsAsync(existingRuleTags, ct);

            foreach (var tagName in tagNames)
            {
                if (!tagIdsByName.TryGetValue(tagName, out var tagId)) continue;
                if (await _photoTagRepository.PhotoTagExistsAsync(photo.Id, tagId, ct)) continue;

                await _photoTagRepository.AddPhotoTagAsync(new PhotoTag
                {
                    PhotoId = photo.Id,
                    TagId = tagId,
                    Source = PhotoTagSource.Rule,
                    CreatedAt = DateTime.UtcNow,
                }, ct);
            }
        }

        await _photoTagRepository.SaveChangesAsync(ct);
    }
}
