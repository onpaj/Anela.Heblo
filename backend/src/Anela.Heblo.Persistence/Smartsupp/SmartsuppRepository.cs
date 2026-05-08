using Anela.Heblo.Domain.Features.Smartsupp;
using Microsoft.EntityFrameworkCore;

namespace Anela.Heblo.Persistence.Smartsupp;

public sealed class SmartsuppRepository : ISmartsuppRepository
{
    private readonly ApplicationDbContext _db;

    public SmartsuppRepository(ApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<(List<SmartsuppConversation> Items, int Total)> ListConversationsAsync(
        SmartsuppConversationStatus status,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        var query = _db.SmartsuppConversations
            .AsNoTracking()
            .Where(c => c.Status == status)
            .OrderByDescending(c => c.LastMessageAt);

        var total = await query.CountAsync(cancellationToken);
        var items = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return (items, total);
    }

    public async Task<SmartsuppConversation?> GetConversationAsync(
        string id,
        CancellationToken cancellationToken) =>
        await _db.SmartsuppConversations
            .AsNoTracking()
            .Include(c => c.Messages.OrderBy(m => m.CreatedAt))
            .FirstOrDefaultAsync(c => c.Id == id, cancellationToken);

    public async Task UpsertConversationAsync(
        SmartsuppConversation conversation,
        CancellationToken cancellationToken)
    {
        var existing = await _db.SmartsuppConversations
            .FirstOrDefaultAsync(c => c.Id == conversation.Id, cancellationToken);

        if (existing is null)
        {
            _db.SmartsuppConversations.Add(conversation);
        }
        else
        {
            existing.Status = conversation.Status;
            existing.IsUnread = conversation.IsUnread;
            existing.ContactName = conversation.ContactName;
            existing.ContactEmail = conversation.ContactEmail;
            existing.ContactAvatarUrl = conversation.ContactAvatarUrl;
            existing.LastMessageAt = conversation.LastMessageAt;
            existing.LastMessagePreview = conversation.LastMessagePreview;
            existing.UpdatedAt = conversation.UpdatedAt;
            existing.SyncedAt = conversation.SyncedAt;
        }
    }

    public async Task UpsertMessagesAsync(
        string conversationId,
        List<SmartsuppMessage> messages,
        CancellationToken cancellationToken)
    {
        var existing = await _db.SmartsuppMessages
            .Where(m => m.ConversationId == conversationId)
            .ToDictionaryAsync(m => m.Id, cancellationToken);

        foreach (var message in messages)
        {
            if (existing.TryGetValue(message.Id, out var tracked))
            {
                tracked.Content = message.Content;
                tracked.AuthorName = message.AuthorName;
                tracked.AttachmentsJson = message.AttachmentsJson;
            }
            else
            {
                _db.SmartsuppMessages.Add(message);
            }
        }
    }

    public async Task<SmartsuppSyncState> GetOrCreateSyncStateAsync(CancellationToken cancellationToken)
    {
        var state = await _db.SmartsuppSyncState.FirstOrDefaultAsync(cancellationToken);
        if (state is not null) return state;

        state = new SmartsuppSyncState { LastSyncStartedAt = DateTime.UtcNow };
        _db.SmartsuppSyncState.Add(state);
        await _db.SaveChangesAsync(cancellationToken);
        return state;
    }

    public async Task SetSyncWatermarkAsync(DateTime lastUpdatedAtSeen, CancellationToken cancellationToken)
    {
        var state = await GetOrCreateSyncStateAsync(cancellationToken);
        state.LastUpdatedAtSeen = lastUpdatedAtSeen;
        state.LastSyncStartedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task SaveChangesAsync(CancellationToken cancellationToken) =>
        await _db.SaveChangesAsync(cancellationToken);
}
