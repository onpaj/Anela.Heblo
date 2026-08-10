using Anela.Heblo.Domain.Features.MindMaps;
using Microsoft.EntityFrameworkCore;

namespace Anela.Heblo.Persistence.MindMaps;

public class MindMapRepository : IMindMapRepository
{
    private readonly ApplicationDbContext _context;

    public MindMapRepository(ApplicationDbContext context)
    {
        _context = context;
    }

    public Task<MindMap?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        return _context.MindMaps
            .Include(x => x.Meetings)
                .ThenInclude(m => m.MeetingTranscript)
            .FirstOrDefaultAsync(x => x.Id == id, ct);
    }

    public Task<MindMap?> GetForUpdateAsync(Guid id, CancellationToken ct = default)
    {
        return _context.MindMaps
            .Include(x => x.Meetings)
                .ThenInclude(m => m.MeetingTranscript)
            .Include(x => x.Versions)
            .FirstOrDefaultAsync(x => x.Id == id, ct);
    }

    public Task<List<MindMapVersionSummary>> GetVersionSummariesAsync(Guid mindMapId, CancellationToken ct = default)
    {
        return _context.MindMapVersions
            .Where(v => v.MindMapId == mindMapId)
            .OrderByDescending(v => v.VersionNumber)
            .Select(v => new MindMapVersionSummary
            {
                VersionNumber = v.VersionNumber,
                CreatedAt = v.CreatedAt,
                TriggerMeetingId = v.TriggerMeetingId
            })
            .ToListAsync(ct);
    }

    public Task<MindMapVersion?> GetVersionAsync(Guid mindMapId, int versionNumber, CancellationToken ct = default)
    {
        return _context.MindMapVersions
            .FirstOrDefaultAsync(v => v.MindMapId == mindMapId && v.VersionNumber == versionNumber, ct);
    }

    public async Task<int> GetNextVersionNumberAsync(Guid mindMapId, CancellationToken ct = default)
    {
        var maxVersionNumber = await _context.MindMapVersions
            .Where(v => v.MindMapId == mindMapId)
            .Select(v => (int?)v.VersionNumber)
            .MaxAsync(ct);
        return (maxVersionNumber ?? 0) + 1;
    }

    public Task<List<MindMap>> GetListAsync(CancellationToken ct = default)
    {
        return _context.MindMaps
            .Include(x => x.Meetings)
            .OrderByDescending(x => x.UpdatedAt)
            .ToListAsync(ct);
    }

    public async Task AddAsync(MindMap map, CancellationToken ct = default)
    {
        await _context.MindMaps.AddAsync(map, ct);
    }

    public Task DeleteAsync(MindMap map, CancellationToken ct = default)
    {
        _context.MindMaps.Remove(map);
        return Task.CompletedTask;
    }

    public Task SetFailedAsync(Guid mindMapId, string lastError, CancellationToken ct = default)
    {
        return _context.MindMaps
            .Where(x => x.Id == mindMapId)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(x => x.Status, MindMapStatus.Failed)
                .SetProperty(x => x.LastError, lastError)
                .SetProperty(x => x.UpdatedAt, DateTime.UtcNow), ct);
    }

    public Task SaveChangesAsync(CancellationToken ct = default)
    {
        return _context.SaveChangesAsync(ct);
    }
}
