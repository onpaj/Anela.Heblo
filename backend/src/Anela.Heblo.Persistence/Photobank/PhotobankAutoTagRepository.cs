using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Anela.Heblo.Domain.Features.Photobank;
using Anela.Heblo.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Anela.Heblo.Persistence.Photobank;

public class PhotobankAutoTagRepository : IPhotobankAutoTagRepository
{
    private readonly ApplicationDbContext _context;

    public PhotobankAutoTagRepository(ApplicationDbContext context)
    {
        _context = context;
    }

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

    public async Task<List<PhotoAutoTagCandidate>> GetPhotoRuleCandidatesPageAsync(
        int pageSize, int offset, CancellationToken cancellationToken)
    {
        return await _context.Photos
            .AsNoTracking()
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

    public async Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        await _context.SaveChangesAsync(cancellationToken);
    }
}
