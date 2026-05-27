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
            .Include(x => x.AccessGrants)
            .FirstOrDefaultAsync(x => x.Id == id, ct);
    }

    public async Task<(List<MeetingTranscript> Items, int TotalCount)> GetListAsync(
        MeetingTranscriptStatus? statusFilter,
        string? searchText,
        bool searchInTranscript,
        bool isManager,
        string? userEmail,
        int page,
        int pageSize,
        CancellationToken ct = default)
    {
        var query = _context.MeetingTranscripts.AsQueryable();

        if (statusFilter.HasValue)
            query = query.Where(x => x.Status == statusFilter.Value);

        if (!string.IsNullOrWhiteSpace(searchText))
        {
            var pattern = $"%{searchText.Trim()}%";
            if (searchInTranscript)
            {
                query = query.Where(x =>
                    EF.Functions.ILike(x.Subject, pattern) ||
                    EF.Functions.ILike(x.Summary, pattern) ||
                    EF.Functions.ILike(x.RawTranscript, pattern));
            }
            else
            {
                query = query.Where(x =>
                    EF.Functions.ILike(x.Subject, pattern) ||
                    EF.Functions.ILike(x.Summary, pattern));
            }
        }

        if (!isManager)
        {
            var email = (userEmail ?? string.Empty).ToLowerInvariant();
            query = query.Where(x =>
                x.AccessLevel == MeetingAccessLevel.Public ||
                (x.AccessLevel == MeetingAccessLevel.Restricted &&
                 x.AccessGrants.Any(g => g.UserEmail.ToLower() == email)));
        }

        var totalCount = await query.CountAsync(ct);

        var items = await query
            .Include(x => x.Tasks)
            .Include(x => x.AccessGrants)
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

    public async Task SetAccessAsync(
        MeetingTranscript transcript,
        MeetingAccessLevel level,
        IReadOnlyList<MeetingAccessGrant> newGrants,
        CancellationToken ct = default)
    {
        _context.MeetingAccessGrants.RemoveRange(transcript.AccessGrants);
        transcript.AccessGrants.Clear();
        transcript.AccessLevel = level;
        await _context.MeetingAccessGrants.AddRangeAsync(newGrants, ct);
    }

    public async Task ReplacePendingTasksAsync(
        MeetingTranscript transcript,
        IReadOnlyList<ProposedTask> newTasks,
        CancellationToken ct = default)
    {
        var pending = transcript.Tasks.Where(t => t.Status == ProposedTaskStatus.Pending).ToList();
        _context.ProposedTasks.RemoveRange(pending);
        foreach (var t in pending)
            transcript.Tasks.Remove(t);

        await _context.ProposedTasks.AddRangeAsync(newTasks, ct);
        transcript.Tasks.AddRange(newTasks);
    }

    public Task SaveChangesAsync(CancellationToken ct = default)
    {
        return _context.SaveChangesAsync(ct);
    }
}
