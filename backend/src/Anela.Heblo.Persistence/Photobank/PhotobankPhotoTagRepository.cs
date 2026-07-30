using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Anela.Heblo.Domain.Features.Photobank;
using Anela.Heblo.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Anela.Heblo.Persistence.Photobank;

public class PhotobankPhotoTagRepository : IPhotobankPhotoTagRepository
{
    private readonly ApplicationDbContext _context;

    public PhotobankPhotoTagRepository(ApplicationDbContext context)
    {
        _context = context;
    }

    public Task AddPhotoTagAsync(PhotoTag photoTag, CancellationToken cancellationToken)
    {
        _context.PhotoTags.Add(photoTag);
        return Task.CompletedTask;
    }

    public Task AddPhotoTagsAsync(IEnumerable<PhotoTag> photoTags, CancellationToken cancellationToken)
    {
        _context.PhotoTags.AddRange(photoTags);
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

    public async Task RemoveRuleTagsAsync(string? scopeToTagName, CancellationToken cancellationToken)
    {
        var query = _context.PhotoTags.Where(pt => pt.Source == PhotoTagSource.Rule);
        if (scopeToTagName != null)
            query = query.Where(pt => pt.Tag.Name == scopeToTagName);

        var ruleTags = await query.ToListAsync(cancellationToken);
        _context.PhotoTags.RemoveRange(ruleTags);
    }

    public async Task<HashSet<(int PhotoId, int TagId)>> GetOccupiedTagPairsAsync(
        string? scopeToTagName, CancellationToken cancellationToken)
    {
        var query = _context.PhotoTags.Where(pt => pt.Source != PhotoTagSource.Rule);
        if (scopeToTagName != null)
            query = query.Where(pt => pt.Tag.Name == scopeToTagName);

        var pairs = await query
            .Select(pt => new { pt.PhotoId, pt.TagId })
            .ToListAsync(cancellationToken);

        return pairs.Select(x => (x.PhotoId, x.TagId)).ToHashSet();
    }

    public async Task<List<PhotoTag>> GetPhotoTagsByPhotoAndSourceAsync(int photoId, PhotoTagSource source, CancellationToken cancellationToken)
    {
        return await _context.PhotoTags
            .Where(pt => pt.PhotoId == photoId && pt.Source == source)
            .ToListAsync(cancellationToken);
    }

    public Task RemovePhotoTagsAsync(IEnumerable<PhotoTag> photoTags, CancellationToken cancellationToken)
    {
        _context.PhotoTags.RemoveRange(photoTags);
        return Task.CompletedTask;
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
