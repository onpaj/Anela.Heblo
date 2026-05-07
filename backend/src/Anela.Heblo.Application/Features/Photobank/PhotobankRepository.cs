using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Anela.Heblo.Domain.Features.Photobank;
using Anela.Heblo.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Anela.Heblo.Application.Features.Photobank
{
    public class PhotobankRepository : IPhotobankRepository
    {
        private readonly ApplicationDbContext _context;

        public PhotobankRepository(ApplicationDbContext context)
        {
            _context = context;
        }

        // Photos

        private IQueryable<Photo> BuildFilterQuery(List<string>? tags, string? search, bool useRegex, string? folderPath, bool useFolderRegex)
        {
            var query = _context.Photos.AsQueryable();

            if (!string.IsNullOrWhiteSpace(search))
            {
                if (useRegex)
                {
                    var pattern = search.Trim();
                    query = query.Where(p => Regex.IsMatch(p.FileName, pattern, RegexOptions.IgnoreCase));
                }
                else
                {
                    var term = search.Trim().ToLowerInvariant();
                    query = query.Where(p => p.FileName.ToLower().Contains(term));
                }
            }

            if (!string.IsNullOrWhiteSpace(folderPath))
            {
                if (useFolderRegex)
                {
                    var pattern = folderPath.Trim();
                    query = query.Where(p => Regex.IsMatch(p.FolderPath, pattern, RegexOptions.IgnoreCase));
                }
                else
                {
                    var pathTerm = folderPath.Trim().ToLowerInvariant();
                    query = query.Where(p => p.FolderPath.ToLower().Contains(pathTerm));
                }
            }

            if (tags != null && tags.Count > 0)
            {
                var normalizedTags = tags
                    .Where(t => !string.IsNullOrWhiteSpace(t))
                    .Select(t => t.Trim().ToLowerInvariant())
                    .ToList();

                foreach (var tag in normalizedTags)
                {
                    var t = tag;
                    query = query.Where(p => p.Tags.Any(pt => pt.Tag.Name == t));
                }
            }

            return query;
        }

        public async Task<(List<Photo> Items, int Total)> GetPhotosAsync(
            List<string>? tags, string? search, bool useRegex, string? folderPath, bool useFolderRegex, bool withoutTags, int page, int pageSize,
            CancellationToken cancellationToken)
        {
            IQueryable<Photo> query = BuildFilterQuery(tags, search, useRegex, folderPath, useFolderRegex)
                .Include(p => p.Tags)
                    .ThenInclude(pt => pt.Tag);

            if (withoutTags)
                query = query.Where(p => !p.Tags.Any());

            var total = await query.CountAsync(cancellationToken);
            var items = await query
                .OrderByDescending(p => p.ModifiedAt)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync(cancellationToken);

            return (items, total);
        }

        public async Task<int> CountFilteredPhotosAsync(
            List<string>? tags, string? search, string? folderPath,
            CancellationToken cancellationToken)
        {
            var query = BuildFilterQuery(tags, search, false, folderPath, false);
            return await query.CountAsync(cancellationToken);
        }

        public async Task<List<int>> GetFilteredPhotoIdsMissingTagAsync(
            List<string>? tags, string? search, string? folderPath, int tagId,
            CancellationToken cancellationToken)
        {
            var query = BuildFilterQuery(tags, search, false, folderPath, false);
            return await query
                .Where(p => !p.Tags.Any(pt => pt.TagId == tagId))
                .Select(p => p.Id)
                .ToListAsync(cancellationToken);
        }

        public async Task<List<int>> GetExistingPhotoIdsMissingTagAsync(
            IReadOnlyList<int> photoIds, int tagId,
            CancellationToken cancellationToken)
        {
            return await _context.Photos
                .Where(p => photoIds.Contains(p.Id) && !p.Tags.Any(pt => pt.TagId == tagId))
                .Select(p => p.Id)
                .ToListAsync(cancellationToken);
        }

        public async Task<int> CountExistingPhotosAsync(
            IReadOnlyList<int> photoIds,
            CancellationToken cancellationToken)
        {
            return await _context.Photos
                .Where(p => photoIds.Contains(p.Id))
                .CountAsync(cancellationToken);
        }

        public async Task<Photo?> GetPhotoByIdAsync(int id, CancellationToken cancellationToken)
        {
            return await _context.Photos
                .Include(p => p.Tags)
                    .ThenInclude(pt => pt.Tag)
                .FirstOrDefaultAsync(p => p.Id == id, cancellationToken);
        }

        public async Task<PhotoLocator?> GetLocatorAsync(int id, CancellationToken cancellationToken)
        {
            var projection = await _context.Photos
                .Where(p => p.Id == id)
                .Select(p => new { p.DriveId, p.SharePointFileId, p.ModifiedAt })
                .FirstOrDefaultAsync(cancellationToken);

            if (projection == null || projection.DriveId == null)
                return null;

            return new PhotoLocator(projection.DriveId, projection.SharePointFileId, projection.ModifiedAt);
        }

        // Tags

        public async Task<List<(Tag Tag, int Count)>> GetTagsWithCountsAsync(CancellationToken cancellationToken)
        {
            var results = await _context.PhotobankTags
                .Select(t => new
                {
                    Tag = t,
                    Count = t.PhotoTags.Count,
                })
                .OrderByDescending(x => x.Count)
                .ToListAsync(cancellationToken);

            return results.Select(x => (x.Tag, x.Count)).ToList();
        }

        private Task<Tag?> FindTagByNameAsync(string normalizedName, CancellationToken ct)
        {
            return _context.PhotobankTags
                .FirstOrDefaultAsync(t => t.Name == normalizedName, ct);
        }

        public async Task<Tag?> GetOrCreateTagAsync(string normalizedName, CancellationToken cancellationToken)
        {
            var existing = await FindTagByNameAsync(normalizedName, cancellationToken);

            if (existing != null)
                return existing;

            var tag = new Tag { Name = normalizedName };
            _context.PhotobankTags.Add(tag);
            await _context.SaveChangesAsync(cancellationToken);
            return tag;
        }

        public async Task<Tag?> GetTagByIdAsync(int id, CancellationToken cancellationToken)
        {
            return await _context.PhotobankTags
                .Include(t => t.PhotoTags)
                .FirstOrDefaultAsync(t => t.Id == id, cancellationToken);
        }

        public Task<Tag?> GetTagByNameAsync(string normalizedName, CancellationToken cancellationToken)
        {
            return FindTagByNameAsync(normalizedName, cancellationToken);
        }

        public Task DeleteTagAsync(Tag tag, CancellationToken cancellationToken)
        {
            _context.PhotobankTags.Remove(tag);
            return Task.CompletedTask;
        }

        // Photo tags

        public Task AddPhotoTagAsync(PhotoTag photoTag, CancellationToken cancellationToken)
        {
            _context.PhotoTags.Add(photoTag);
            return Task.CompletedTask;
        }

        public async Task RemovePhotoTagAsync(int photoId, int tagId, CancellationToken cancellationToken)
        {
            var photoTag = await _context.PhotoTags
                .FindAsync(new object[] { photoId, tagId }, cancellationToken);
            if (photoTag != null)
                _context.PhotoTags.Remove(photoTag);
        }

        public async Task<bool> PhotoTagExistsAsync(int photoId, int tagId, CancellationToken cancellationToken)
        {
            return await _context.PhotoTags
                .AnyAsync(pt => pt.PhotoId == photoId && pt.TagId == tagId, cancellationToken);
        }

        // Roots

        public async Task<List<PhotobankIndexRoot>> GetRootsAsync(CancellationToken cancellationToken)
        {
            return await _context.PhotobankIndexRoots
                .OrderBy(r => r.CreatedAt)
                .ToListAsync(cancellationToken);
        }

        public Task<PhotobankIndexRoot> AddRootAsync(PhotobankIndexRoot root, CancellationToken cancellationToken)
        {
            _context.PhotobankIndexRoots.Add(root);
            return Task.FromResult(root);
        }

        public async Task<bool> DeleteRootAsync(int id, CancellationToken cancellationToken)
        {
            var root = await _context.PhotobankIndexRoots.FindAsync(new object[] { id }, cancellationToken);
            if (root == null)
                return false;
            _context.PhotobankIndexRoots.Remove(root);
            return true;
        }

        // Rules

        public async Task<List<TagRule>> GetRulesAsync(CancellationToken cancellationToken)
        {
            return await _context.PhotobankTagRules
                .OrderBy(r => r.SortOrder)
                .ThenBy(r => r.Id)
                .ToListAsync(cancellationToken);
        }

        public Task<TagRule> AddRuleAsync(TagRule rule, CancellationToken cancellationToken)
        {
            _context.PhotobankTagRules.Add(rule);
            return Task.FromResult(rule);
        }

        public async Task<TagRule?> GetRuleByIdAsync(int id, CancellationToken cancellationToken)
        {
            return await _context.PhotobankTagRules.FindAsync(new object[] { id }, cancellationToken);
        }

        public Task UpdateRuleAsync(TagRule rule, CancellationToken cancellationToken)
        {
            _context.PhotobankTagRules.Update(rule);
            return Task.CompletedTask;
        }

        public async Task<bool> DeleteRuleAsync(int id, CancellationToken cancellationToken)
        {
            var rule = await _context.PhotobankTagRules.FindAsync(new object[] { id }, cancellationToken);
            if (rule == null)
                return false;
            _context.PhotobankTagRules.Remove(rule);
            return true;
        }

        // Reapply rules

        public async Task<int> ReapplyRulesAsync(List<TagRule> allRules, CancellationToken cancellationToken)
        {
            var activeRules = allRules.Where(r => r.IsActive).ToList();

            // Remove all Rule-sourced PhotoTags
            var ruleTags = await _context.PhotoTags
                .Where(pt => pt.Source == PhotoTagSource.Rule)
                .ToListAsync(cancellationToken);
            _context.PhotoTags.RemoveRange(ruleTags);

            // Load all photos to re-evaluate
            var photos = await _context.Photos.ToListAsync(cancellationToken);

            var photosUpdated = 0;
            var now = DateTime.UtcNow;

            foreach (var photo in photos)
            {
                var matchingTagNames = TagRuleMatcher.GetMatchingTags(photo.FolderPath, photo.FileName, activeRules);
                if (matchingTagNames.Count == 0)
                    continue;

                var tagsUpdated = false;
                foreach (var tagName in matchingTagNames)
                {
                    var tag = await GetOrCreateTagAsync(tagName, cancellationToken);
                    if (tag == null)
                        continue;

                    _context.PhotoTags.Add(new PhotoTag
                    {
                        PhotoId = photo.Id,
                        TagId = tag.Id,
                        Source = PhotoTagSource.Rule,
                        CreatedAt = now,
                    });
                    tagsUpdated = true;
                }

                if (tagsUpdated)
                    photosUpdated++;
            }

            return photosUpdated;
        }

        // Auto-tagging

        public async Task<List<PhotoAutoTagCandidate>> GetPhotosPendingAutoTagAsync(
            int pageSize, int offset, CancellationToken cancellationToken)
        {
            return await _context.Photos
                .Where(p => p.LastAutoTaggedAt == null)
                .OrderBy(p => p.Id)
                .Skip(offset)
                .Take(pageSize)
                .Select(p => new PhotoAutoTagCandidate(p.Id, p.FolderPath, p.FileName))
                .ToListAsync(cancellationToken);
        }

        public async Task StampAutoTaggedAtAsync(
            IReadOnlyList<int> photoIds, DateTime timestamp, CancellationToken cancellationToken)
        {
            await _context.Photos
                .Where(p => photoIds.Contains(p.Id))
                .ExecuteUpdateAsync(
                    s => s.SetProperty(p => p.LastAutoTaggedAt, timestamp),
                    cancellationToken);
        }

        public async Task ResetAutoTaggedAtAsync(
            IReadOnlyList<int> photoIds, CancellationToken cancellationToken)
        {
            await _context.Photos
                .Where(p => photoIds.Contains(p.Id))
                .ExecuteUpdateAsync(
                    s => s.SetProperty(p => p.LastAutoTaggedAt, (DateTime?)null),
                    cancellationToken);
        }

        public async Task<List<Photo>> GetPhotosByIdsAsync(
            IReadOnlyList<int> photoIds, CancellationToken cancellationToken)
        {
            return await _context.Photos
                .Where(p => photoIds.Contains(p.Id))
                .ToListAsync(cancellationToken);
        }

        public async Task RemovePhotoTagsBySourceAsync(
            IReadOnlyList<int> photoIds, PhotoTagSource source, CancellationToken cancellationToken)
        {
            await _context.PhotoTags
                .Where(pt => photoIds.Contains(pt.PhotoId) && pt.Source == source)
                .ExecuteDeleteAsync(cancellationToken);
        }

        public async Task SaveChangesAsync(CancellationToken cancellationToken)
        {
            await _context.SaveChangesAsync(cancellationToken);
        }
    }
}
