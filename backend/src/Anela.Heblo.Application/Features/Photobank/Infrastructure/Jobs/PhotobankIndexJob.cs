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
            photo!.LastAutoTaggedAt = null;

        await _repo.SaveChangesAsync(ct);

        // Re-apply rule tags: remove existing Rule-source tags, add new ones
        var existingRuleTags = await _repo.GetPhotoTagsByPhotoAndSourceAsync(photo.Id, PhotoTagSource.Rule, ct);
        await _repo.RemovePhotoTagsAsync(existingRuleTags, ct);

        var matchingTagNames = TagRuleMatcher.GetMatchingTags(item.FolderPath, item.Name, tagRules);
        foreach (var tagName in matchingTagNames)
        {
            var tag = await _repo.GetOrCreateTagAsync(tagName, ct);

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
