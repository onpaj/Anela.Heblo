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
            .Include(x => x.Versions)
            .FirstOrDefaultAsync(x => x.Id == id, ct);
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
