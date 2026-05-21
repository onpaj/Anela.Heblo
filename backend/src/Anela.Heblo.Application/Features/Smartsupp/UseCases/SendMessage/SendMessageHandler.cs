using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Smartsupp;
using Anela.Heblo.Domain.Features.Users;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Anela.Heblo.Application.Features.Smartsupp.UseCases.SendMessage;

public class SendMessageHandler : IRequestHandler<SendMessageRequest, SendMessageResponse>
{
    private readonly ISmartsuppRepository _repository;
    private readonly ISmartsuppApiClient _apiClient;
    private readonly ICurrentUserService _currentUserService;
    private readonly IOptions<SmartsuppSendMessageOptions> _options;
    private readonly ILogger<SendMessageHandler> _logger;

    public SendMessageHandler(
        ISmartsuppRepository repository,
        ISmartsuppApiClient apiClient,
        ICurrentUserService currentUserService,
        IOptions<SmartsuppSendMessageOptions> options,
        ILogger<SendMessageHandler> logger)
    {
        _repository = repository;
        _apiClient = apiClient;
        _currentUserService = currentUserService;
        _options = options;
        _logger = logger;
    }

    public async Task<SendMessageResponse> Handle(
        SendMessageRequest request,
        CancellationToken cancellationToken)
    {
        var conversation = await _repository.GetConversationAsync(request.ConversationId, cancellationToken);
        if (conversation is null)
            return new SendMessageResponse(ErrorCodes.SmartsuppConversationNotFound);

        var currentUser = _currentUserService.GetCurrentUser();
        if (string.IsNullOrWhiteSpace(currentUser.Email)
            || !_options.Value.AgentMap.TryGetValue(currentUser.Email, out var agentId))
        {
            _logger.LogWarning(
                "Smartsupp send blocked: no agent mapping found for {Email}.",
                currentUser.Email);
            return new SendMessageResponse(ErrorCodes.SmartsuppAgentMappingNotFound);
        }

        SmartsuppSentMessageData sent;
        try
        {
            sent = await _apiClient.SendMessageAsync(
                request.ConversationId, request.Content, agentId, cancellationToken);
        }
        // Only absorb cancellations that originated inside the API client (e.g. HttpClient timeouts),
        // not cancellations requested by the caller.
        catch (Exception ex) when (ex is HttpRequestException or TimeoutException
                                       or ObjectDisposedException
                                       || (ex is TaskCanceledException tce && tce.CancellationToken != cancellationToken))
        {
            _logger.LogWarning(ex, "Smartsupp API unavailable while sending message to {ConversationId}",
                request.ConversationId);
            return new SendMessageResponse(ErrorCodes.SmartsuppSendMessageUnavailable);
        }

        return new SendMessageResponse
        {
            MessageId = sent.Id,
            CreatedAt = sent.CreatedAt,
        };
    }
}
