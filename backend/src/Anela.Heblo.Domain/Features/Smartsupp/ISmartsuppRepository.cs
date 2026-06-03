namespace Anela.Heblo.Domain.Features.Smartsupp;

public sealed record OpenConversationRef(string Id, DateTime? LastMessageAt);

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

    Task BackfillConversationDenormFieldsAsync(
        SmartsuppContact contact,
        CancellationToken cancellationToken);

    Task UpsertConversationAsync(
        SmartsuppConversation conversation,
        CancellationToken cancellationToken);

    Task UpsertMessagesAsync(
        string conversationId,
        List<SmartsuppMessage> messages,
        CancellationToken cancellationToken);

    Task<List<OpenConversationRef>> ListOpenConversationRefsAsync(
        CancellationToken cancellationToken);

    Task<List<SmartsuppConversation>> ListConversationsForContactAsync(
        string contactId,
        string excludeConversationId,
        CancellationToken cancellationToken);

    Task MarkConversationResolvedAsync(
        string conversationId,
        DateTime finishedAt,
        DateTime syncedAt,
        CancellationToken cancellationToken);

    Task UpdateMessageDeliveryStatusAsync(
        string messageId,
        string status,
        DateTime? deliveredAt,
        CancellationToken cancellationToken);

    Task SaveChangesAsync(CancellationToken cancellationToken);

    void DiscardChanges();

    Task UpdateVisitorCacheAsync(
        string conversationId,
        string? userAgent,
        string? os,
        string? browser,
        string? browserVersion,
        int? visitsCount,
        DateTime fetchedAt,
        CancellationToken cancellationToken);
}
