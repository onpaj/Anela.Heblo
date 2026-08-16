using Anela.Heblo.Domain.Features.Smartsupp;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.Smartsupp.Infrastructure;

/// <summary>
/// Resolves a SmartsuppConversation's ContactId to a locally-persisted SmartsuppContact, fetching
/// and staging it via the Smartsupp REST API when it is not already known locally. On any failure
/// to resolve (REST error or REST returns null), clears conversation.ContactId so the caller
/// persists an unlinked conversation (fail-open — matches pre-refactor SmartsuppRepository
/// behaviour; see #3878).
/// </summary>
public interface ISmartsuppContactEnricher
{
    Task<SmartsuppConversation> EnrichContactAsync(
        SmartsuppConversation conversation,
        CancellationToken cancellationToken);
}

public sealed class SmartsuppContactEnricher : ISmartsuppContactEnricher
{
    private readonly ISmartsuppApiClient _apiClient;
    private readonly ISmartsuppRepository _repository;
    private readonly ILogger<SmartsuppContactEnricher> _logger;

    public SmartsuppContactEnricher(
        ISmartsuppApiClient apiClient,
        ISmartsuppRepository repository,
        ILogger<SmartsuppContactEnricher> logger)
    {
        _apiClient = apiClient;
        _repository = repository;
        _logger = logger;
    }

    public async Task<SmartsuppConversation> EnrichContactAsync(
        SmartsuppConversation conversation,
        CancellationToken cancellationToken)
    {
        if (conversation.ContactId is null)
            return conversation;

        var existsLocally = await _repository.ContactExistsAsync(conversation.ContactId, cancellationToken);
        if (existsLocally)
            return conversation;

        // Smartsupp webhooks reference contacts by id without inlining the name/email
        // and we cannot rely on a contact.* event arriving — pull the record via REST so
        // the FK link survives and the conversation row carries the display name.
        SmartsuppContactData? data;
        try
        {
            data = await _apiClient.GetContactAsync(conversation.ContactId, cancellationToken);
        }
        catch (Exception ex)
        {
            // Fail open: webhook still saves the conversation without the contact link.
            // The orphan backfill job can pick it up later when Smartsupp REST is healthy.
            _logger.LogWarning(ex,
                "smartsupp: failed to fetch contact {ContactId} while upserting conversation; continuing without link",
                conversation.ContactId);
            conversation.ContactId = null;
            return conversation;
        }

        if (data is null)
        {
            _logger.LogWarning(
                "smartsupp: contact {ContactId} not found via REST while upserting conversation; continuing without link",
                conversation.ContactId);
            conversation.ContactId = null;
            return conversation;
        }

        var contact = MapContactDataToEntity(data, conversation.SyncedAt);
        await _repository.UpsertContactAsync(contact, cancellationToken);

        conversation.ContactName ??= contact.Name;
        conversation.ContactEmail ??= contact.Email;
        return conversation;
    }

    // Timestamps MUST be DateTimeKind.Utc: UpsertContactAsync writes them via
    // ExecuteSqlInterpolated, which types a bare DateTime as `timestamp with time zone`
    // and rejects Kind=Unspecified at the Npgsql layer. The webhook contact path
    // (SmartsuppPayloadMapper.MapContact) already produces Utc; this REST-staged path
    // must match, otherwise the enclosing conversation upsert throws and the conversation
    // is dropped (observed for Facebook Messenger contacts fetched on demand).
    internal static SmartsuppContact MapContactDataToEntity(SmartsuppContactData data, DateTime syncedAt) =>
        new()
        {
            Id = data.Id,
            Email = data.Email,
            Name = data.Name,
            Phone = data.Phone,
            Note = data.Note,
            BannedAt = data.BannedAt is { } bannedAt ? DateTime.SpecifyKind(bannedAt, DateTimeKind.Utc) : null,
            BannedBy = data.BannedBy,
            GdprApproved = data.GdprApproved,
            TagsJson = data.TagsJson,
            PropertiesJson = data.PropertiesJson,
            CreatedAt = DateTime.SpecifyKind(data.CreatedAt, DateTimeKind.Utc),
            UpdatedAt = DateTime.SpecifyKind(data.UpdatedAt, DateTimeKind.Utc),
            SyncedAt = DateTime.SpecifyKind(syncedAt, DateTimeKind.Utc),
        };
}
