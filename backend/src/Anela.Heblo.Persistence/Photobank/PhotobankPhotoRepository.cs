using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Anela.Heblo.Domain.Features.Photobank;
using Anela.Heblo.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Anela.Heblo.Persistence.Photobank;

public class PhotobankPhotoRepository : IPhotobankPhotoRepository
{
    private readonly ApplicationDbContext _context;

    public PhotobankPhotoRepository(ApplicationDbContext context)
    {
        _context = context;
    }

    private IQueryable<Photo> BuildFilterQuery(List<string>? tags, string? search, bool useRegex)
    {
        var query = _context.Photos.AsQueryable();

        if (!string.IsNullOrWhiteSpace(search))
        {
            if (useRegex)
            {
                var pattern = search.Trim();
                query = query.Where(p => Regex.IsMatch(p.FolderPath + "/" + p.FileName, pattern, RegexOptions.IgnoreCase));
            }
            else
            {
                var term = search.Trim().ToLowerInvariant();
                query = query.Where(p => (p.FolderPath + "/" + p.FileName).ToLower().Contains(term));
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
        List<string>? tags, string? search, bool useRegex, bool withoutTags, int page, int pageSize,
        CancellationToken cancellationToken)
    {
        IQueryable<Photo> query = BuildFilterQuery(tags, search, useRegex)
            .Include(p => p.Tags)
                .ThenInclude(pt => pt.Tag);

        if (withoutTags)
            query = query.Where(p => !p.Tags.Any());

        try
        {
            var total = await query.CountAsync(cancellationToken);
            var items = await query
                .OrderByDescending(p => p.ModifiedAt)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync(cancellationToken);

            return (items, total);
        }
        catch (PostgresException ex) when (useRegex && ex.SqlState == "2201B")
        {
            throw new InvalidPhotoSearchPatternException(search ?? string.Empty);
        }
    }

    public async Task<int> CountFilteredPhotosAsync(
        List<string>? tags, string? search,
        CancellationToken cancellationToken)
    {
        var query = BuildFilterQuery(tags, search, false);
        return await query.CountAsync(cancellationToken);
    }

    public async Task<List<int>> GetFilteredPhotoIdsMissingTagAsync(
        List<string>? tags, string? search, int tagId,
        CancellationToken cancellationToken)
    {
        var query = BuildFilterQuery(tags, search, false);
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

    public Task<Photo?> GetPhotoBySharePointFileIdAsync(string sharePointFileId, CancellationToken cancellationToken)
    {
        return _context.Photos
            .FirstOrDefaultAsync(p => p.SharePointFileId == sharePointFileId, cancellationToken);
    }

    public Task AddPhotoAsync(Photo photo, CancellationToken cancellationToken)
    {
        _context.Photos.Add(photo);
        return Task.CompletedTask;
    }

    public Task RemovePhotoAsync(Photo photo, CancellationToken cancellationToken)
    {
        _context.Photos.Remove(photo);
        return Task.CompletedTask;
    }

    public async Task<List<Photo>> GetPhotosByIdsAsync(
        IReadOnlyList<int> photoIds, CancellationToken cancellationToken)
    {
        return await _context.Photos
            .Where(p => photoIds.Contains(p.Id))
            .ToListAsync(cancellationToken);
    }

    public async Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        await _context.SaveChangesAsync(cancellationToken);
    }
}
