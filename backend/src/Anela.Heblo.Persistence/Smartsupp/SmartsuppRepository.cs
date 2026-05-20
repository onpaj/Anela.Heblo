using Anela.Heblo.Domain.Features.Smartsupp;
using Microsoft.EntityFrameworkCore;

namespace Anela.Heblo.Persistence.Smartsupp;

public sealed class SmartsuppRepository : ISmartsuppRepository
{
    private const int MaxOtherConversations = 20;

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
            .Include(c => c.Contact)
            .FirstOrDefaultAsync(c => c.Id == id, cancellationToken);

    public async Task UpsertContactAsync(
        SmartsuppContact contact,
        CancellationToken cancellationToken)
    {
        var existing = await _db.SmartsuppContacts
            .FirstOrDefaultAsync(c => c.Id == contact.Id, cancellationToken);

        if (existing is null)
        {
            _db.SmartsuppContacts.Add(contact);
        }
        else
        {
            if (existing.UpdatedAt > contact.UpdatedAt)
            {
                return;
            }

            existing.Email = contact.Email;
            existing.Name = contact.Name;
            existing.Phone = contact.Phone;
            existing.Note = contact.Note;
            existing.BannedAt = contact.BannedAt;
            existing.BannedBy = contact.BannedBy;
            existing.GdprApproved = contact.GdprApproved;
            existing.TagsJson = contact.TagsJson;
            existing.PropertiesJson = contact.PropertiesJson;
            existing.UpdatedAt = contact.UpdatedAt;
            existing.SyncedAt = contact.SyncedAt;
        }
    }

    public async Task UpsertConversationAsync(
        SmartsuppConversation conversation,
        CancellationToken cancellationToken)
    {
        SmartsuppContact? linkedContact = null;
        if (conversation.ContactId is not null)
        {
            linkedContact = await _db.SmartsuppContacts
                .AsNoTracking()
                .FirstOrDefaultAsync(c => c.Id == conversation.ContactId, cancellationToken);

            if (linkedContact is null)
                conversation.ContactId = null;
        }

        conversation.ContactName ??= linkedContact?.Name;
        conversation.ContactEmail ??= linkedContact?.Email;

        var existing = await _db.SmartsuppConversations
            .FirstOrDefaultAsync(c => c.Id == conversation.Id, cancellationToken);

        if (existing is null)
        {
            _db.SmartsuppConversations.Add(conversation);
            return;
        }

        if (existing.UpdatedAt > conversation.UpdatedAt)
        {
            // Out-of-order event: stored state is fresher — skip update.
            return;
        }

        existing.Status = conversation.Status;
        existing.IsUnread = conversation.IsUnread;
        existing.IsOffline = conversation.IsOffline;
        existing.IsServed = conversation.IsServed;
        existing.ContactId = conversation.ContactId;
        existing.Subject = conversation.Subject;
        existing.ContactName = conversation.ContactName ?? existing.ContactName;
        existing.ContactEmail = conversation.ContactEmail ?? existing.ContactEmail;
        existing.ContactAvatarUrl = conversation.ContactAvatarUrl ?? existing.ContactAvatarUrl;
        existing.VisitorId = conversation.VisitorId;
        existing.ExtId = conversation.ExtId;
        existing.FinishedAt = conversation.FinishedAt;
        existing.Domain = conversation.Domain;
        existing.Referer = conversation.Referer;
        existing.LocationCountry = conversation.LocationCountry;
        existing.LocationCity = conversation.LocationCity;
        existing.LocationIp = conversation.LocationIp;
        existing.LocationCode = conversation.LocationCode;
        existing.VariablesJson = conversation.VariablesJson;
        existing.TagsJson = conversation.TagsJson;
        existing.LastMessageAt = conversation.LastMessageAt;
        existing.LastMessagePreview = conversation.LastMessagePreview;
        existing.UpdatedAt = conversation.UpdatedAt;
        existing.SyncedAt = conversation.SyncedAt;
        existing.Rating = conversation.Rating;
        existing.RatingText = conversation.RatingText;
        existing.CloseType = conversation.CloseType;
        existing.ClosedByAgentId = conversation.ClosedByAgentId;
        existing.AssignedAgentIdsJson = conversation.AssignedAgentIdsJson;
        existing.Channel = conversation.Channel;
        existing.LastClosedAt = conversation.LastClosedAt;
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
                tracked.SubType = message.SubType;
                tracked.TriggerName = message.TriggerName;
                tracked.TriggerId = message.TriggerId;
                tracked.PageUrl = message.PageUrl;
                tracked.AgentId = message.AgentId;
                tracked.VisitorId = message.VisitorId;
                tracked.DeliveryStatus = message.DeliveryStatus;
                tracked.DeliveredAt = message.DeliveredAt;
                tracked.IsOffline = message.IsOffline;
                tracked.IsReply = message.IsReply;
                tracked.IsFirstReply = message.IsFirstReply;
                tracked.ResponseTime = message.ResponseTime;
                tracked.UpdatedAt = message.UpdatedAt;
                tracked.AttachmentsJson = message.AttachmentsJson;
            }
            else
            {
                _db.SmartsuppMessages.Add(message);
            }
        }
    }

    public async Task<List<OpenConversationRef>> ListOpenConversationRefsAsync(
        CancellationToken cancellationToken) =>
        await _db.SmartsuppConversations
            .AsNoTracking()
            .Where(c => c.Status == SmartsuppConversationStatus.Open)
            .Select(c => new OpenConversationRef(c.Id, c.LastMessageAt))
            .ToListAsync(cancellationToken);

    public async Task<List<SmartsuppConversation>> ListConversationsForContactAsync(
        string contactId,
        string excludeConversationId,
        CancellationToken cancellationToken) =>
        await _db.SmartsuppConversations
            .AsNoTracking()
            .Where(c => c.ContactId == contactId && c.Id != excludeConversationId)
            .OrderByDescending(c => c.LastMessageAt)
            .Take(MaxOtherConversations)
            .ToListAsync(cancellationToken);

    public async Task MarkConversationResolvedAsync(
        string conversationId,
        DateTime finishedAt,
        DateTime syncedAt,
        CancellationToken cancellationToken)
    {
        var existing = await _db.SmartsuppConversations
            .FirstOrDefaultAsync(c => c.Id == conversationId, cancellationToken);

        if (existing is null)
            return;

        existing.Status = SmartsuppConversationStatus.Resolved;
        existing.FinishedAt = finishedAt;
        existing.SyncedAt = syncedAt;
    }

    public async Task UpdateMessageDeliveryStatusAsync(
        string messageId,
        string status,
        DateTime? deliveredAt,
        CancellationToken cancellationToken)
    {
        var existing = await _db.SmartsuppMessages
            .FirstOrDefaultAsync(m => m.Id == messageId, cancellationToken);

        if (existing is null)
            return;

        existing.DeliveryStatus = status;
        existing.DeliveredAt = deliveredAt;
    }


    public async Task BackfillConversationDenormFieldsAsync(
        SmartsuppContact contact,
        CancellationToken cancellationToken)
    {
        var conversations = await _db.SmartsuppConversations
            .Where(c => c.ContactId == contact.Id)
            .ToListAsync(cancellationToken);

        foreach (var conv in conversations)
        {
            conv.ContactName = contact.Name ?? conv.ContactName;
            conv.ContactEmail = contact.Email ?? conv.ContactEmail;
        }
    }

    public Task UpdateVisitorCacheAsync(
        string conversationId,
        string? userAgent,
        string? os,
        string? browser,
        string? browserVersion,
        int? visitsCount,
        DateTime fetchedAt,
        CancellationToken cancellationToken) =>
        throw new NotImplementedException("Implemented in Task 5.");

    public async Task SaveChangesAsync(CancellationToken cancellationToken) =>
        await _db.SaveChangesAsync(cancellationToken);
}
