using Anela.Heblo.Domain.Features.Smartsupp;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Anela.Heblo.Persistence.Smartsupp;

public sealed class SmartsuppRepository : ISmartsuppRepository
{
    private const int MaxOtherConversations = 20;

    private readonly ApplicationDbContext _db;
    private readonly ISmartsuppApiClient _apiClient;
    private readonly ILogger<SmartsuppRepository> _logger;

    public SmartsuppRepository(
        ApplicationDbContext db,
        ISmartsuppApiClient apiClient,
        ILogger<SmartsuppRepository> logger)
    {
        _db = db;
        _apiClient = apiClient;
        _logger = logger;
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
            .OrderByDescending(c => c.LastMessageAt ?? c.UpdatedAt);

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
        // Raw SQL: column list must match the EF mapping in SmartsuppContactConfiguration.
        // See memory/gotchas/raw-sql-insert-must-match-ef-mapping.md when adding columns.
        await _db.Database.ExecuteSqlInterpolatedAsync(
            $@"INSERT INTO public.""SmartsuppContacts"" (
                    ""Id"", ""Email"", ""Name"", ""Phone"", ""Note"",
                    ""BannedAt"", ""BannedBy"", ""GdprApproved"", ""TagsJson"", ""PropertiesJson"",
                    ""CreatedAt"", ""UpdatedAt"", ""SyncedAt"")
                VALUES (
                    {contact.Id}, {contact.Email}, {contact.Name}, {contact.Phone}, {contact.Note},
                    {contact.BannedAt}, {contact.BannedBy}, {contact.GdprApproved}, {contact.TagsJson}, {contact.PropertiesJson},
                    {contact.CreatedAt}, {contact.UpdatedAt}, {contact.SyncedAt})
                ON CONFLICT (""Id"") DO UPDATE
                    SET ""Email""          = EXCLUDED.""Email"",
                        ""Name""           = EXCLUDED.""Name"",
                        ""Phone""          = EXCLUDED.""Phone"",
                        ""Note""           = EXCLUDED.""Note"",
                        ""BannedAt""       = EXCLUDED.""BannedAt"",
                        ""BannedBy""       = EXCLUDED.""BannedBy"",
                        ""GdprApproved""   = EXCLUDED.""GdprApproved"",
                        ""TagsJson""       = EXCLUDED.""TagsJson"",
                        ""PropertiesJson"" = EXCLUDED.""PropertiesJson"",
                        ""UpdatedAt""      = EXCLUDED.""UpdatedAt"",
                        ""SyncedAt""       = EXCLUDED.""SyncedAt""
                WHERE EXCLUDED.""UpdatedAt"" >= ""SmartsuppContacts"".""UpdatedAt""",
            cancellationToken);
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
            {
                // Smartsupp webhooks reference contacts by id without inlining the name/email
                // and we cannot rely on a contact.* event arriving — pull the record via REST so
                // the FK link survives and the conversation row carries the display name.
                linkedContact = await TryFetchAndStageContactAsync(
                    conversation.ContactId, conversation.SyncedAt, cancellationToken);

                if (linkedContact is null)
                    conversation.ContactId = null;
            }
        }

        conversation.ContactName ??= linkedContact?.Name;
        conversation.ContactEmail ??= linkedContact?.Email;

        var status = conversation.Status.ToString();

        // Raw SQL: column list must match the EF mapping in SmartsuppConversationConfiguration.
        // See memory/gotchas/raw-sql-insert-must-match-ef-mapping.md when adding columns.
        // Status is stored as string (HasConversion<string>()) so must be passed as .ToString().
        // COALESCE on ContactName/ContactEmail/ContactAvatarUrl preserves existing non-null values
        // when the incoming event carries null for those fields.
        await _db.Database.ExecuteSqlInterpolatedAsync(
            $@"INSERT INTO public.""SmartsuppConversations"" (
                    ""Id"", ""ExtId"", ""Subject"", ""ContactId"",
                    ""ContactName"", ""ContactEmail"", ""ContactAvatarUrl"", ""VisitorId"",
                    ""Status"", ""IsUnread"", ""IsOffline"", ""IsServed"",
                    ""FinishedAt"", ""Domain"", ""Referer"",
                    ""LocationCountry"", ""LocationCity"", ""LocationIp"", ""LocationCode"",
                    ""VariablesJson"", ""TagsJson"",
                    ""LastMessageAt"", ""LastMessagePreview"",
                    ""CreatedAt"", ""UpdatedAt"", ""SyncedAt"",
                    ""Rating"", ""RatingText"", ""CloseType"", ""ClosedByAgentId"",
                    ""AssignedAgentIdsJson"", ""Channel"", ""LastClosedAt"",
                    ""VisitorUserAgent"", ""VisitorOs"", ""VisitorBrowser"",
                    ""VisitorBrowserVersion"", ""VisitorVisitsCount"", ""VisitorInfoFetchedAt"")
                VALUES (
                    {conversation.Id}, {conversation.ExtId}, {conversation.Subject}, {conversation.ContactId},
                    {conversation.ContactName}, {conversation.ContactEmail}, {conversation.ContactAvatarUrl}, {conversation.VisitorId},
                    {status}, {conversation.IsUnread}, {conversation.IsOffline}, {conversation.IsServed},
                    {conversation.FinishedAt}, {conversation.Domain}, {conversation.Referer},
                    {conversation.LocationCountry}, {conversation.LocationCity}, {conversation.LocationIp}, {conversation.LocationCode},
                    {conversation.VariablesJson}, {conversation.TagsJson},
                    {conversation.LastMessageAt}, {conversation.LastMessagePreview},
                    {conversation.CreatedAt}, {conversation.UpdatedAt}, {conversation.SyncedAt},
                    {conversation.Rating}, {conversation.RatingText}, {conversation.CloseType}, {conversation.ClosedByAgentId},
                    {conversation.AssignedAgentIdsJson}, {conversation.Channel}, {conversation.LastClosedAt},
                    {conversation.VisitorUserAgent}, {conversation.VisitorOs}, {conversation.VisitorBrowser},
                    {conversation.VisitorBrowserVersion}, {conversation.VisitorVisitsCount}, {conversation.VisitorInfoFetchedAt})
                ON CONFLICT (""Id"") DO UPDATE
                    SET ""ExtId""               = EXCLUDED.""ExtId"",
                        ""Subject""             = EXCLUDED.""Subject"",
                        ""ContactId""           = EXCLUDED.""ContactId"",
                        ""ContactName""         = COALESCE(EXCLUDED.""ContactName"",    ""SmartsuppConversations"".""ContactName""),
                        ""ContactEmail""        = COALESCE(EXCLUDED.""ContactEmail"",   ""SmartsuppConversations"".""ContactEmail""),
                        ""ContactAvatarUrl""    = COALESCE(EXCLUDED.""ContactAvatarUrl"", ""SmartsuppConversations"".""ContactAvatarUrl""),
                        ""VisitorId""           = EXCLUDED.""VisitorId"",
                        ""Status""              = EXCLUDED.""Status"",
                        ""IsUnread""            = EXCLUDED.""IsUnread"",
                        ""IsOffline""           = EXCLUDED.""IsOffline"",
                        ""IsServed""            = EXCLUDED.""IsServed"",
                        ""FinishedAt""          = EXCLUDED.""FinishedAt"",
                        ""Domain""              = EXCLUDED.""Domain"",
                        ""Referer""             = EXCLUDED.""Referer"",
                        ""LocationCountry""     = EXCLUDED.""LocationCountry"",
                        ""LocationCity""        = EXCLUDED.""LocationCity"",
                        ""LocationIp""          = EXCLUDED.""LocationIp"",
                        ""LocationCode""        = EXCLUDED.""LocationCode"",
                        ""VariablesJson""       = EXCLUDED.""VariablesJson"",
                        ""TagsJson""            = EXCLUDED.""TagsJson"",
                        ""LastMessageAt""       = EXCLUDED.""LastMessageAt"",
                        ""LastMessagePreview""  = EXCLUDED.""LastMessagePreview"",
                        ""UpdatedAt""           = EXCLUDED.""UpdatedAt"",
                        ""SyncedAt""            = EXCLUDED.""SyncedAt"",
                        ""Rating""              = EXCLUDED.""Rating"",
                        ""RatingText""          = EXCLUDED.""RatingText"",
                        ""CloseType""           = EXCLUDED.""CloseType"",
                        ""ClosedByAgentId""     = EXCLUDED.""ClosedByAgentId"",
                        ""AssignedAgentIdsJson"" = EXCLUDED.""AssignedAgentIdsJson"",
                        ""Channel""             = EXCLUDED.""Channel"",
                        ""LastClosedAt""        = EXCLUDED.""LastClosedAt"",
                        ""VisitorUserAgent""    = EXCLUDED.""VisitorUserAgent"",
                        ""VisitorOs""           = EXCLUDED.""VisitorOs"",
                        ""VisitorBrowser""      = EXCLUDED.""VisitorBrowser"",
                        ""VisitorBrowserVersion"" = EXCLUDED.""VisitorBrowserVersion"",
                        ""VisitorVisitsCount""  = EXCLUDED.""VisitorVisitsCount"",
                        ""VisitorInfoFetchedAt"" = EXCLUDED.""VisitorInfoFetchedAt""
                WHERE EXCLUDED.""UpdatedAt"" >= ""SmartsuppConversations"".""UpdatedAt""",
            cancellationToken);
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

    public async Task<List<string>> ListOrphanContactConversationIdsAsync(
        CancellationToken cancellationToken) =>
        await _db.SmartsuppConversations
            .AsNoTracking()
            .Where(c => c.ContactName == null && c.ContactEmail == null)
            .Select(c => c.Id)
            .ToListAsync(cancellationToken);

    private async Task<SmartsuppContact?> TryFetchAndStageContactAsync(
        string contactId,
        DateTime syncedAt,
        CancellationToken cancellationToken)
    {
        SmartsuppContactData? data;
        try
        {
            data = await _apiClient.GetContactAsync(contactId, cancellationToken);
        }
        catch (Exception ex)
        {
            // Fail open: webhook still saves the conversation without the contact link.
            // The orphan backfill job can pick it up later when Smartsupp REST is healthy.
            _logger.LogWarning(ex,
                "smartsupp: failed to fetch contact {ContactId} while upserting conversation; continuing without link",
                contactId);
            return null;
        }

        if (data is null)
            return null;

        var contact = MapContactDataToEntity(data, syncedAt);
        await UpsertContactAsync(contact, cancellationToken);
        return contact;
    }

    private static SmartsuppContact MapContactDataToEntity(SmartsuppContactData data, DateTime syncedAt) =>
        new()
        {
            Id = data.Id,
            Email = data.Email,
            Name = data.Name,
            Phone = data.Phone,
            Note = data.Note,
            BannedAt = data.BannedAt,
            BannedBy = data.BannedBy,
            GdprApproved = data.GdprApproved,
            TagsJson = data.TagsJson,
            PropertiesJson = data.PropertiesJson,
            CreatedAt = DateTime.SpecifyKind(data.CreatedAt, DateTimeKind.Unspecified),
            UpdatedAt = DateTime.SpecifyKind(data.UpdatedAt, DateTimeKind.Unspecified),
            SyncedAt = DateTime.SpecifyKind(syncedAt, DateTimeKind.Unspecified),
        };

    public async Task UpdateVisitorCacheAsync(
        string conversationId,
        string? userAgent,
        string? os,
        string? browser,
        string? browserVersion,
        int? visitsCount,
        DateTime fetchedAt,
        CancellationToken cancellationToken)
    {
        var conversation = await _db.SmartsuppConversations
            .FirstOrDefaultAsync(c => c.Id == conversationId, cancellationToken);

        if (conversation is null)
            return;

        conversation.VisitorUserAgent = userAgent;
        conversation.VisitorOs = os;
        conversation.VisitorBrowser = browser;
        conversation.VisitorBrowserVersion = browserVersion;
        conversation.VisitorVisitsCount = visitsCount;
        conversation.VisitorInfoFetchedAt = fetchedAt;
        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task SaveChangesAsync(CancellationToken cancellationToken) =>
        await _db.SaveChangesAsync(cancellationToken);

    // Clears all staged EF changes before a retry. Safe because HandleAsync is idempotent —
    // all repository calls re-read from the DB before staging new state.
    public void DiscardChanges() => _db.ChangeTracker.Clear();
}
