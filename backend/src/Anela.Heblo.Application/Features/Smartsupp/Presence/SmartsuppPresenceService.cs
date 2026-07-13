using Anela.Heblo.Application.Features.Smartsupp.Contracts;
using Anela.Heblo.Application.Features.Smartsupp.UseCases.SendMessage;
using Anela.Heblo.Domain.Features.Smartsupp;
using Anela.Heblo.Domain.Features.Users;
using Microsoft.Extensions.Options;

namespace Anela.Heblo.Application.Features.Smartsupp.Presence;

public sealed class SmartsuppPresenceService : ISmartsuppPresenceService
{
    private readonly ISmartsuppPresenceRepository _repository;
    private readonly ICurrentUserService _currentUserService;
    private readonly IOptions<SmartsuppSendMessageOptions> _sendMessageOptions;
    private readonly IOptions<SmartsuppPresenceOptions> _presenceOptions;

    public SmartsuppPresenceService(
        ISmartsuppPresenceRepository repository,
        ICurrentUserService currentUserService,
        IOptions<SmartsuppSendMessageOptions> sendMessageOptions,
        IOptions<SmartsuppPresenceOptions> presenceOptions)
    {
        _repository = repository;
        _currentUserService = currentUserService;
        _sendMessageOptions = sendMessageOptions;
        _presenceOptions = presenceOptions;
    }

    public async Task RecordCurrentOperatorAsync(string conversationId, CancellationToken cancellationToken)
    {
        var operatorIdentity = ResolveCurrentOperator();
        if (operatorIdentity is null)
            return;

        var now = UtcNowUnspecified();
        await _repository.UpsertAsync(
            new SmartsuppConversationPresence
            {
                ConversationId = conversationId,
                AgentId = operatorIdentity.Value.Key,
                DisplayName = operatorIdentity.Value.DisplayName,
                Source = SmartsuppPresenceSource.Heblo,
                EnteredAt = now,
                LastSeenAt = now,
            },
            cancellationToken);
    }

    public async Task RemoveCurrentOperatorAsync(string conversationId, CancellationToken cancellationToken)
    {
        var operatorIdentity = ResolveCurrentOperator();
        if (operatorIdentity is null)
            return;

        await _repository.RemoveAsync(
            conversationId, operatorIdentity.Value.Key, SmartsuppPresenceSource.Heblo, cancellationToken);
    }

    public async Task<Dictionary<string, List<ConversationPresenceDto>>> GetActiveViewersAsync(
        IReadOnlyCollection<string> conversationIds,
        CancellationToken cancellationToken)
    {
        if (conversationIds.Count == 0)
            return new Dictionary<string, List<ConversationPresenceDto>>();

        var now = UtcNowUnspecified();
        var hebloThreshold = now.AddSeconds(-_presenceOptions.Value.HeartbeatTtlSeconds);
        var smartsuppThreshold = now.AddMinutes(-_presenceOptions.Value.SmartsuppTtlMinutes);

        var active = await _repository.GetActiveByConversationAsync(
            conversationIds, hebloThreshold, smartsuppThreshold, cancellationToken);

        var currentKey = ResolveCurrentOperator()?.Key;

        return active.ToDictionary(
            entry => entry.Key,
            entry => MapViewers(entry.Value, currentKey));
    }

    private static List<ConversationPresenceDto> MapViewers(
        IEnumerable<SmartsuppConversationPresence> rows,
        string? currentKey) =>
        rows
            // One operator may be present from both sources (native app + Heblo tab); collapse to one entry.
            .GroupBy(p => p.AgentId)
            .Select(g => g.OrderBy(p => p.EnteredAt).First())
            .OrderBy(p => p.EnteredAt)
            .Select(p => new ConversationPresenceDto
            {
                AgentId = p.AgentId,
                DisplayName = p.DisplayName,
                Source = p.Source.ToString(),
                IsCurrentUser = currentKey is not null
                    && string.Equals(p.AgentId, currentKey, StringComparison.OrdinalIgnoreCase),
                EnteredAt = p.EnteredAt,
            })
            .ToList();

    /// <summary>
    /// Resolve the calling operator's stable identity key and display name. The key is their mapped
    /// Smartsupp agent id when known (so native + Heblo presence collapse to one person), else their
    /// email. Returns null when the caller has no usable identity (unauthenticated).
    /// </summary>
    private (string Key, string DisplayName)? ResolveCurrentOperator()
    {
        var user = _currentUserService.GetCurrentUser();
        var email = user.Email;

        string? key = null;
        if (!string.IsNullOrWhiteSpace(email)
            && _sendMessageOptions.Value.AgentMap.TryGetValue(email, out var agentId)
            && !string.IsNullOrWhiteSpace(agentId))
        {
            key = agentId;
        }
        else if (!string.IsNullOrWhiteSpace(email))
        {
            key = email;
        }
        else if (!string.IsNullOrWhiteSpace(user.Id))
        {
            key = user.Id;
        }

        if (key is null)
            return null;

        var displayName = !string.IsNullOrWhiteSpace(user.Name)
            ? user.Name!
            : email ?? key;

        return (key, displayName);
    }

    private static DateTime UtcNowUnspecified() =>
        DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified);
}
