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

        var syncState = await _repository.GetOrCreateSyncStateAsync(cancellationToken);
        var updatedAfter = syncState.LastUpdatedAtSeen;

        var totalUpserted = 0;
        string? cursor = null;
        DateTime? latestUpdatedAt = null;

        do
        {
            SmartsuppSearchResult page;

            try
            {
                page = await _apiClient.SearchConversationsAsync(updatedAfter, cursor, PageSize, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "smartsupp-sync failed to fetch page (cursor={Cursor}), aborting run", cursor);
                return;
            }

            foreach (var item in page.Items)
            {
                await ProcessConversationAsync(item, syncedAt, cancellationToken);
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

    private async Task ProcessConversationAsync(SmartsuppConversationData data, DateTime syncedAt, CancellationToken cancellationToken)
    {
        var status = data.Status?.ToLowerInvariant() == "resolved"
            ? SmartsuppConversationStatus.Resolved
            : SmartsuppConversationStatus.Open;

        var conversation = new SmartsuppConversation
        {
            Id = data.Id,
            Status = status,
            IsUnread = data.Unread,
            ContactName = data.ContactName,
            ContactEmail = data.ContactEmail,
            ContactAvatarUrl = data.ContactAvatarUrl,
            LastMessagePreview = data.LastMessageText?.Length > LastMessagePreviewMaxLength
                ? data.LastMessageText[..LastMessagePreviewMaxLength]
                : data.LastMessageText,
            LastMessageAt = data.LastMessageAt,
            CreatedAt = data.CreatedAt,
            UpdatedAt = data.UpdatedAt,
            SyncedAt = syncedAt,
        };

        await _repository.UpsertConversationAsync(conversation, cancellationToken);

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
            return;
        }

        var messages = messageData.Select(m => new SmartsuppMessage
        {
            Id = m.Id,
            ConversationId = data.Id,
            AuthorType = ParseAuthorType(m.AuthorType),
            AuthorName = m.AuthorName,
            Content = m.Content,
            CreatedAt = m.CreatedAt,
        }).ToList();

        await _repository.UpsertMessagesAsync(data.Id, messages, cancellationToken);
    }

    private static SmartsuppMessageAuthorType ParseAuthorType(string? type) =>
        type?.ToLowerInvariant() switch
        {
            "agent" => SmartsuppMessageAuthorType.Agent,
            "bot" => SmartsuppMessageAuthorType.Bot,
            _ => SmartsuppMessageAuthorType.Visitor,
        };
}
