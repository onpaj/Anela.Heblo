using Anela.Heblo.Domain.Features.Smartsupp;
using Anela.Heblo.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.Smartsupp.UseCases.RefreshOrphanContacts;

public class RefreshOrphanContactsHandler
    : IRequestHandler<RefreshOrphanContactsRequest, RefreshOrphanContactsResponse>
{
    private readonly ISmartsuppRepository _repository;
    private readonly ISmartsuppApiClient _apiClient;
    private readonly ApplicationDbContext _db;
    private readonly ILogger<RefreshOrphanContactsHandler> _logger;

    public RefreshOrphanContactsHandler(
        ISmartsuppRepository repository,
        ISmartsuppApiClient apiClient,
        ApplicationDbContext db,
        ILogger<RefreshOrphanContactsHandler> logger)
    {
        _repository = repository;
        _apiClient = apiClient;
        _db = db;
        _logger = logger;
    }

    public async Task<RefreshOrphanContactsResponse> Handle(
        RefreshOrphanContactsRequest request,
        CancellationToken cancellationToken)
    {
        var response = new RefreshOrphanContactsResponse();
        var ids = await _repository.ListOrphanContactConversationIdsAsync(cancellationToken);
        response.Scanned = ids.Count;

        foreach (var conversationId in ids)
        {
            try
            {
                var remote = await _apiClient.GetConversationAsync(conversationId, cancellationToken);
                if (remote?.ContactId is null)
                {
                    response.SkippedNoContactId++;
                    continue;
                }

                var local = await _db.SmartsuppConversations
                    .FirstOrDefaultAsync(c => c.Id == conversationId, cancellationToken);
                if (local is null)
                {
                    response.SkippedNoContactId++;
                    continue;
                }

                // Re-attach the contact_id Smartsupp still knows about and let UpsertConversationAsync
                // pull the contact via REST (same path as the runtime fix).
                local.ContactId = remote.ContactId;
                local.SyncedAt = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified);

                await _repository.UpsertConversationAsync(local, cancellationToken);
                await _repository.SaveChangesAsync(cancellationToken);

                response.Updated++;
            }
            catch (Exception ex)
            {
                response.Failed++;
                response.FailedIds.Add(conversationId);
                _logger.LogError(ex,
                    "smartsupp: orphan-contacts backfill failed for conversation {ConversationId}",
                    conversationId);
                _db.ChangeTracker.Clear();
            }
        }

        _logger.LogInformation(
            "smartsupp orphan-contacts backfill done: scanned={Scanned} updated={Updated} skipped={Skipped} failed={Failed}",
            response.Scanned, response.Updated, response.SkippedNoContactId, response.Failed);

        return response;
    }
}
