using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Anela.Heblo.Domain.Features.Photobank;
using Microsoft.EntityFrameworkCore;

namespace Anela.Heblo.Persistence.Photobank
{
    public class PhotobankRepository : IPhotobankRepository
    {
        private readonly ApplicationDbContext _context;

        public PhotobankRepository(ApplicationDbContext context)
        {
            _context = context;
        }

        // Photos

        public async Task<(List<Photo> Items, int Total)> GetPhotosAsync(
            List<string>? tags, string? search, int page, int pageSize,
            CancellationToken cancellationToken)
        {
            var query = _context.Photos
                .Include(p => p.Tags)
                    .ThenInclude(pt => pt.Tag)
                .AsQueryable();

            if (!string.IsNullOrWhiteSpace(search))
            {
                var term = search.Trim().ToLowerInvariant();
                query = query.Where(p => p.FileName.ToLower().Contains(term));
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

            var total = await query.CountAsync(cancellationToken);
            var items = await query
                .OrderByDescending(p => p.ModifiedAt)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync(cancellationToken);

            return (items, total);
        }

        public async Task<Photo?> GetPhotoByIdAsync(int id, CancellationToken cancellationToken)
        {
            return await _context.Photos
                .Include(p => p.Tags)
                    .ThenInclude(pt => pt.Tag)
                .FirstOrDefaultAsync(p => p.Id == id, cancellationToken);
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

        public async Task<Tag?> GetOrCreateTagAsync(string normalizedName, CancellationToken cancellationToken)
        {
            var existing = await _context.PhotobankTags
                .FirstOrDefaultAsync(t => t.Name == normalizedName, cancellationToken);

            if (existing != null)
                return existing;

            var tag = new Tag { Name = normalizedName };
            _context.PhotobankTags.Add(tag);
            await _context.SaveChangesAsync(cancellationToken);
            return tag;
        }

        public async Task<Tag?> GetTagByIdAsync(int id, CancellationToken cancellationToken)
        {
            return await _context.PhotobankTags.FindAsync(new object[] { id }, cancellationToken);
        }

        // Photo tags

        public async Task AddPhotoTagAsync(PhotoTag photoTag, CancellationToken cancellationToken)
        {
            _context.PhotoTags.Add(photoTag);
            await Task.CompletedTask;
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

        public async Task<PhotobankIndexRoot> AddRootAsync(PhotobankIndexRoot root, CancellationToken cancellationToken)
        {
            _context.PhotobankIndexRoots.Add(root);
            await Task.CompletedTask;
            return root;
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

        public async Task<TagRule> AddRuleAsync(TagRule rule, CancellationToken cancellationToken)
        {
            _context.PhotobankTagRules.Add(rule);
            await Task.CompletedTask;
            return rule;
        }

        public async Task<TagRule?> GetRuleByIdAsync(int id, CancellationToken cancellationToken)
        {
            return await _context.PhotobankTagRules.FindAsync(new object[] { id }, cancellationToken);
        }

        public async Task UpdateRuleAsync(TagRule rule, CancellationToken cancellationToken)
        {
            _context.PhotobankTagRules.Update(rule);
            await Task.CompletedTask;
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
                var matchingTagNames = TagRuleMatcher.GetMatchingTags(photo.FolderPath, activeRules);
                if (matchingTagNames.Count == 0)
                    continue;

                var tagsUpdated = false;
                foreach (var tagName in matchingTagNames)
                {
                    var tag = await GetOrCreateTagAsync(tagName, cancellationToken);
                    _context.PhotoTags.Add(new PhotoTag
                    {
                        PhotoId = photo.Id,
                        TagId = tag!.Id,
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

        public async Task SaveChangesAsync(CancellationToken cancellationToken)
        {
            await _context.SaveChangesAsync(cancellationToken);
        }
    }
}
