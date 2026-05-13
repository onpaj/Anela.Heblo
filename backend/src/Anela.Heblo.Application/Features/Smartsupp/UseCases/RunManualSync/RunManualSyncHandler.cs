using Anela.Heblo.Domain.Features.Smartsupp;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.Smartsupp.UseCases.RunManualSync;

public class RunManualSyncHandler : IRequestHandler<RunManualSyncRequest, RunManualSyncResponse>
{
    private const int PageSize = 50;
    private const int LastMessagePreviewMaxLength = 200;
    private const int DefaultLookbackDays = 7;
    private const int MaxLookbackDays = 30;

    private readonly ISmartsuppApiClient _apiClient;
    private readonly ISmartsuppRepository _repository;
    private readonly ILogger<RunManualSyncHandler> _logger;

    public RunManualSyncHandler(
        ISmartsuppApiClient apiClient,
        ISmartsuppRepository repository,
        ILogger<RunManualSyncHandler> logger)
    {
        _apiClient = apiClient;
        _repository = repository;
        _logger = logger;
    }

    public async Task<RunManualSyncResponse> Handle(
        RunManualSyncRequest request,
        CancellationToken cancellationToken)
    {
        var startedAt = DateTime.UtcNow;
        var since = ResolveSince(request.Since, startedAt);

        _logger.LogInformation("smartsupp manual sync starting since={Since}", since);

        var contactCache = new Dictionary<string, SmartsuppContactData?>(StringComparer.Ordinal);
        var conversationsProcessed = 0;
        var messagesProcessed = 0;
        string? cursor = null;

        do
        {
            SmartsuppSearchResult page;
            try
            {
                page = await _apiClient.SearchConversationsAsync(cursor, PageSize, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "smartsupp manual sync failed to fetch page (cursor={Cursor})", cursor);
                break;
            }

            _logger.LogDebug("smartsupp manual sync page items={Count} after={After}", page.Items.Count, page.After);

            foreach (var item in page.Items)
            {
                if (item.UpdatedAt <= since)
                    continue;

                var msgCount = await ProcessConversationAsync(item, startedAt, contactCache, cancellationToken);
                conversationsProcessed++;
                messagesProcessed += msgCount;
            }

            await _repository.SaveChangesAsync(cancellationToken);
            cursor = page.After;

        } while (cursor is not null);

        var completedAt = DateTime.UtcNow;
        _logger.LogInformation(
            "smartsupp manual sync completed conversations={Conversations} messages={Messages}",
            conversationsProcessed, messagesProcessed);

        return new RunManualSyncResponse
        {
            ConversationsProcessed = conversationsProcessed,
            MessagesProcessed = messagesProcessed,
            StartedAt = startedAt,
            CompletedAt = completedAt,
        };
    }

    private static DateTime ResolveSince(DateTime? requested, DateTime now)
    {
        var floor = now.AddDays(-MaxLookbackDays);
        var defaultSince = now.AddDays(-DefaultLookbackDays);
        var requestedOrDefault = requested ?? defaultSince;
        return requestedOrDefault < floor ? floor : requestedOrDefault;
    }

    private async Task<int> ProcessConversationAsync(
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
            _logger.LogWarning(ex, "smartsupp manual sync failed to fetch messages for {ConversationId}", data.Id);
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
            return 0;

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
        return messages.Count;
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
            _logger.LogWarning(ex, "smartsupp manual sync failed to fetch contact {ContactId}", contactId);
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
