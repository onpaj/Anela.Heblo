using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Smartsupp;
using Anela.Heblo.Domain.Features.Users;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.Smartsupp.UseCases.SendMessage;

public class SendMessageHandler : IRequestHandler<SendMessageRequest, SendMessageResponse>
{
    private readonly ISmartsuppRepository _repository;
    private readonly ISmartsuppApiClient _apiClient;
    private readonly ICurrentUserService _currentUserService;
    private readonly ILogger<SendMessageHandler> _logger;

    public SendMessageHandler(
        ISmartsuppRepository repository,
        ISmartsuppApiClient apiClient,
        ICurrentUserService currentUserService,
        ILogger<SendMessageHandler> logger)
    {
        _repository = repository;
        _apiClient = apiClient;
        _currentUserService = currentUserService;
        _logger = logger;
    }

    public async Task<SendMessageResponse> Handle(
        SendMessageRequest request,
        CancellationToken cancellationToken)
    {
        var conversation = await _repository.GetConversationAsync(request.ConversationId, cancellationToken);
        if (conversation is null)
            return new SendMessageResponse(ErrorCodes.SmartsuppConversationNotFound);

        var agentName = SmartsuppNameHelper.ExtractFirstName(_currentUserService.GetCurrentUser().Name);

        SmartsuppSentMessageData sent;
        try
        {
            sent = await _apiClient.SendMessageAsync(
                request.ConversationId, request.Content, agentName, cancellationToken);
        }
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
