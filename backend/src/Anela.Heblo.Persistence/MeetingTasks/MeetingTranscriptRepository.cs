using Anela.Heblo.Domain.Features.MeetingTasks;
using Microsoft.EntityFrameworkCore;

namespace Anela.Heblo.Persistence.MeetingTasks;

public class MeetingTranscriptRepository : IMeetingTranscriptRepository
{
    private readonly ApplicationDbContext _context;

    public MeetingTranscriptRepository(ApplicationDbContext context)
    {
        _context = context;
    }

    public Task<MeetingTranscript?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        return _context.MeetingTranscripts
            .Include(x => x.Tasks)
            .FirstOrDefaultAsync(x => x.Id == id, ct);
    }

    public async Task<(List<MeetingTranscript> Items, int TotalCount)> GetListAsync(
        MeetingTranscriptStatus? statusFilter,
        int page,
        int pageSize,
        CancellationToken ct = default)
    {
        var query = _context.MeetingTranscripts.AsQueryable();

        if (statusFilter.HasValue)
        {
            query = query.Where(x => x.Status == statusFilter.Value);
        }

        var totalCount = await query.CountAsync(ct);

        var items = await query
            .Include(x => x.Tasks)
            .OrderByDescending(x => x.PlaudCreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        return (items, totalCount);
    }

    public Task<bool> ExistsByPlaudIdAsync(string plaudRecordingId, CancellationToken ct = default)
    {
        return _context.MeetingTranscripts
            .AnyAsync(x => x.PlaudRecordingId == plaudRecordingId, ct);
    }

    public async Task AddAsync(MeetingTranscript transcript, CancellationToken ct = default)
    {
        await _context.MeetingTranscripts.AddAsync(transcript, ct);
    }

    public Task SaveChangesAsync(CancellationToken ct = default)
    {
        return _context.SaveChangesAsync(ct);
    }
}
