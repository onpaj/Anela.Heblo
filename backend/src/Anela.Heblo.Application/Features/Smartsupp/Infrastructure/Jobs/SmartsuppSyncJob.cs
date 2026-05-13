using Anela.Heblo.Domain.Features.BackgroundJobs;
using Anela.Heblo.Domain.Features.Smartsupp;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.Smartsupp.Infrastructure.Jobs;

public class SmartsuppSyncJob : IRecurringJob
{
    private const int PageSize = 50;
    private const int LastMessagePreviewMaxLength = 200;

    public RecurringJobMetadata Metadata { get; } = new()
    {
        JobName = "smartsupp-sync",
        DisplayName = "Smartsupp Sync",
        Description = "Polls Smartsupp API for updated conversations and syncs them to the local database",
        CronExpression = "*/2 * * * *",
        DefaultIsEnabled = false,
    };

    private readonly ISmartsuppApiClient _apiClient;
    private readonly ISmartsuppRepository _repository;
    private readonly ILogger<SmartsuppSyncJob> _logger;

    public SmartsuppSyncJob(
        ISmartsuppApiClient apiClient,
        ISmartsuppRepository repository,
        ILogger<SmartsuppSyncJob> logger)
    {
        _apiClient = apiClient;
        _repository = repository;
        _logger = logger;
    }

    public async Task ExecuteAsync(CancellationToken cancellationToken = default)
    {
        var syncedAt = DateTime.UtcNow;

        _logger.LogInformation("smartsupp-sync starting");

        var totalUpserted = 0;
        string? cursor = null;
        DateTime? latestUpdatedAt = null;
        var contactCache = new Dictionary<string, SmartsuppContactData?>(StringComparer.Ordinal);

        do
        {
            SmartsuppSearchResult page;

            try
            {
                page = await _apiClient.SearchConversationsAsync(cursor, PageSize, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "smartsupp-sync failed to fetch page (cursor={Cursor}), aborting run", cursor);
                return;
            }

            foreach (var item in page.Items)
            {
                await ProcessConversationAsync(item, syncedAt, contactCache, cancellationToken);
                totalUpserted++;

                if (latestUpdatedAt is null || item.UpdatedAt > latestUpdatedAt)
                    latestUpdatedAt = item.UpdatedAt;
            }

            await _repository.SaveChangesAsync(cancellationToken);
            cursor = page.After;

        } while (cursor is not null);

        if (latestUpdatedAt.HasValue)
            await _repository.SetSyncWatermarkAsync(latestUpdatedAt.Value, cancellationToken);

        _logger.LogInformation("smartsupp-sync completed — {Count} conversations upserted", totalUpserted);
    }

    private async Task ProcessConversationAsync(
        SmartsuppConversationData data,
        DateTime syncedAt,
        Dictionary<string, SmartsuppContactData?> contactCache,
        CancellationToken cancellationToken)
    {
        List<SmartsuppMessageData> messageData;
        try
        {
            messageData = await _apiClient.GetConversationMessagesAsync(data.Id, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Failed to fetch messages for conversation {ConversationId} — conversation upserted without messages",
                data.Id);
            messageData = [];
        }

        SmartsuppContactData? contact = null;
        if (!string.IsNullOrEmpty(data.ContactId))
        {
            contact = await FetchContactCachedAsync(data.ContactId, contactCache, cancellationToken);

            if (contact is not null)
            {
                var contactEntity = new SmartsuppContact
                {
                    Id = contact.Id,
                    Email = contact.Email,
                    Name = contact.Name,
                    Phone = contact.Phone,
                    Note = contact.Note,
                    BannedAt = contact.BannedAt is { } ba ? Unspecified(ba) : null,
                    BannedBy = contact.BannedBy,
                    GdprApproved = contact.GdprApproved,
                    TagsJson = contact.TagsJson,
                    PropertiesJson = contact.PropertiesJson,
                    CreatedAt = Unspecified(contact.CreatedAt),
                    UpdatedAt = Unspecified(contact.UpdatedAt),
                    SyncedAt = Unspecified(syncedAt),
                };
                await _repository.UpsertContactAsync(contactEntity, cancellationToken);
            }
        }

        var status = data.Status?.ToLowerInvariant() == "resolved"
            ? SmartsuppConversationStatus.Resolved
            : SmartsuppConversationStatus.Open;

        var conversation = new SmartsuppConversation
        {
            Id = data.Id,
            ExtId = data.ExtId,
            Status = status,
            IsUnread = data.Unread,
            IsOffline = data.IsOffline,
            IsServed = data.IsServed,
            ContactId = data.ContactId,
            ContactName = contact?.Name,
            ContactEmail = contact?.Email,
            ContactAvatarUrl = null,
            VisitorId = data.VisitorId,
            FinishedAt = data.FinishedAt is { } fa ? Unspecified(fa) : null,
            Domain = data.Domain,
            Referer = data.Referer,
            LocationCountry = data.LocationCountry,
            LocationCity = data.LocationCity,
            LocationIp = data.LocationIp,
            LocationCode = data.LocationCode,
            VariablesJson = data.VariablesJson,
            TagsJson = data.TagsJson,
            LastMessagePreview = data.LastMessageText?.Length > LastMessagePreviewMaxLength
                ? data.LastMessageText[..LastMessagePreviewMaxLength]
                : data.LastMessageText,
            LastMessageAt = data.LastMessageAt,
            CreatedAt = data.CreatedAt,
            UpdatedAt = data.UpdatedAt,
            SyncedAt = syncedAt,
        };

        await _repository.UpsertConversationAsync(conversation, cancellationToken);

        if (messageData.Count == 0)
            return;

        var messages = messageData.Select(m => new SmartsuppMessage
        {
            Id = m.Id,
            ConversationId = data.Id,
            AuthorType = ParseAuthorType(m.SubType),
            SubType = m.SubType,
            AuthorName = ComposeAuthorName(m, contact),
            Content = m.Content,
            TriggerName = m.TriggerName,
            TriggerId = m.TriggerId,
            PageUrl = m.PageUrl,
            AgentId = m.AgentId,
            VisitorId = m.VisitorId,
            DeliveryStatus = m.DeliveryStatus,
            DeliveredAt = m.DeliveredAt is { } da ? Unspecified(da) : null,
            IsOffline = m.IsOffline,
            IsReply = m.IsReply,
            IsFirstReply = m.IsFirstReply,
            ResponseTime = m.ResponseTime,
            CreatedAt = m.CreatedAt,
            UpdatedAt = m.UpdatedAt,
            AttachmentsJson = m.AttachmentsJson,
        }).ToList();

        await _repository.UpsertMessagesAsync(data.Id, messages, cancellationToken);
    }

    private async Task<SmartsuppContactData?> FetchContactCachedAsync(
        string contactId,
        Dictionary<string, SmartsuppContactData?> cache,
        CancellationToken cancellationToken)
    {
        if (cache.TryGetValue(contactId, out var cached))
            return cached;

        SmartsuppContactData? contact;
        try
        {
            contact = await _apiClient.GetContactAsync(contactId, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to fetch contact {ContactId} — proceeding without contact data", contactId);
            contact = null;
        }

        cache[contactId] = contact;
        return contact;
    }

    private static string? ComposeAuthorName(SmartsuppMessageData message, SmartsuppContactData? contact) =>
        ParseAuthorType(message.SubType) switch
        {
            SmartsuppMessageAuthorType.Visitor => contact?.Name,
            SmartsuppMessageAuthorType.Bot => message.TriggerName,
            _ => null
        };

    private static SmartsuppMessageAuthorType ParseAuthorType(string? subType) =>
        subType?.ToLowerInvariant() switch
        {
            "agent" => SmartsuppMessageAuthorType.Agent,
            "bot" => SmartsuppMessageAuthorType.Bot,
            "contact" => SmartsuppMessageAuthorType.Visitor,
            _ => SmartsuppMessageAuthorType.Visitor,
        };

    private static DateTime Unspecified(DateTime dt) =>
        DateTime.SpecifyKind(dt, DateTimeKind.Unspecified);
}
