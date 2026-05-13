namespace Anela.Heblo.Domain.Features.Smartsupp;

public interface ISmartsuppRepository
{
    Task<(List<SmartsuppConversation> Items, int Total)> ListConversationsAsync(
        SmartsuppConversationStatus status,
        int page,
        int pageSize,
        CancellationToken cancellationToken);

    Task<SmartsuppConversation?> GetConversationAsync(
        string id,
        CancellationToken cancellationToken);

    Task UpsertContactAsync(
        SmartsuppContact contact,
        CancellationToken cancellationToken);

    Task UpsertConversationAsync(
        SmartsuppConversation conversation,
        CancellationToken cancellationToken);

    Task UpsertMessagesAsync(
        string conversationId,
        List<SmartsuppMessage> messages,
        CancellationToken cancellationToken);

    Task<SmartsuppSyncState> GetOrCreateSyncStateAsync(CancellationToken cancellationToken);

    Task SetSyncWatermarkAsync(DateTime lastUpdatedAtSeen, CancellationToken cancellationToken);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}
