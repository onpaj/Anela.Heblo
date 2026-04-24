using Anela.Heblo.Application.Features.Photobank.Services;
using Anela.Heblo.Domain.Features.BackgroundJobs;
using Anela.Heblo.Domain.Features.Photobank;
using Anela.Heblo.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.Photobank.Infrastructure.Jobs;

public class PhotobankIndexJob : IRecurringJob
{
    private readonly IPhotobankGraphService _graphService;
    private readonly ApplicationDbContext _db;
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
        ApplicationDbContext db,
        IRecurringJobStatusChecker statusChecker,
        ILogger<PhotobankIndexJob> logger)
    {
        _graphService = graphService;
        _db = db;
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

        var roots = await _db.PhotobankIndexRoots
            .Where(r => r.IsActive && r.DriveId != null && r.RootItemId != null)
            .ToListAsync(cancellationToken);

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
            var delta = await _graphService.GetDeltaAsync(root.DriveId!, root.RootItemId!, root.DeltaLink, ct);

            var activeTagRules = await _db.PhotobankTagRules
                .Where(r => r.IsActive)
                .OrderBy(r => r.SortOrder)
                .ToListAsync(ct);

            int upserted = 0, deleted = 0;

            foreach (var item in delta.Items)
            {
                if (item.IsDeleted)
                {
                    var existing = await _db.Photos.FirstOrDefaultAsync(p => p.SharePointFileId == item.ItemId, ct);
                    if (existing != null)
                    {
                        _db.Photos.Remove(existing);
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
            await _db.SaveChangesAsync(ct);

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
        var photo = await _db.Photos.FirstOrDefaultAsync(p => p.SharePointFileId == item.ItemId, ct);

        if (photo == null)
        {
            photo = new Photo
            {
                SharePointFileId = item.ItemId,
                IndexedAt = DateTime.UtcNow,
            };
            _db.Photos.Add(photo);
        }

        photo.FileName = item.Name;
        photo.FolderPath = item.FolderPath;
        photo.SharePointWebUrl = item.WebUrl;
        photo.FileSizeBytes = item.FileSizeBytes;
        photo.ModifiedAt = item.LastModifiedAt ?? DateTime.UtcNow;
        photo.DriveId = driveId;

        await _db.SaveChangesAsync(ct);

        // Re-apply rule tags: remove existing Rule-source tags, add new ones
        var existingRuleTags = await _db.PhotoTags
            .Where(pt => pt.PhotoId == photo.Id && pt.Source == PhotoTagSource.Rule)
            .ToListAsync(ct);
        _db.PhotoTags.RemoveRange(existingRuleTags);

        var matchingTagNames = TagRuleMatcher.GetMatchingTags(item.FolderPath, tagRules);
        foreach (var tagName in matchingTagNames)
        {
            var tag = await _db.PhotobankTags.FirstOrDefaultAsync(t => t.Name == tagName, ct)
                      ?? await CreateTagAsync(tagName, ct);

            _db.PhotoTags.Add(new PhotoTag
            {
                PhotoId = photo.Id,
                TagId = tag.Id,
                Source = PhotoTagSource.Rule,
                CreatedAt = DateTime.UtcNow,
            });
        }

        await _db.SaveChangesAsync(ct);
    }

    private async Task<Tag> CreateTagAsync(string name, CancellationToken ct)
    {
        var tag = new Tag { Name = name };
        _db.PhotobankTags.Add(tag);
        await _db.SaveChangesAsync(ct);
        return tag;
    }
}
