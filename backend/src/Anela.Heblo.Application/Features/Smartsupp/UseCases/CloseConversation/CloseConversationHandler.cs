using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Smartsupp;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.Smartsupp.UseCases.CloseConversation;

public class CloseConversationHandler : IRequestHandler<CloseConversationRequest, CloseConversationResponse>
{
    private readonly ISmartsuppRepository _repository;
    private readonly ISmartsuppApiClient _apiClient;
    private readonly ILogger<CloseConversationHandler> _logger;

    public CloseConversationHandler(
        ISmartsuppRepository repository,
        ISmartsuppApiClient apiClient,
        ILogger<CloseConversationHandler> logger)
    {
        _repository = repository;
        _apiClient = apiClient;
        _logger = logger;
    }

    public async Task<CloseConversationResponse> Handle(
        CloseConversationRequest request,
        CancellationToken cancellationToken)
    {
        var conversation = await _repository.GetConversationAsync(request.ConversationId, cancellationToken);
        if (conversation is null)
            return new CloseConversationResponse(ErrorCodes.SmartsuppConversationNotFound);

        try
        {
            await _apiClient.CloseConversationAsync(request.ConversationId, cancellationToken);
        }
        // Only absorb cancellations that originated inside the API client (e.g. HttpClient timeouts),
        // not cancellations requested by the caller.
        catch (Exception ex) when (ex is HttpRequestException or TimeoutException
                                       or ObjectDisposedException
                                       || (ex is TaskCanceledException tce && tce.CancellationToken != cancellationToken))
        {
            _logger.LogWarning(ex, "Smartsupp API unavailable while closing conversation {ConversationId}",
                request.ConversationId);
            return new CloseConversationResponse(ErrorCodes.SmartsuppCloseConversationUnavailable);
        }

        return new CloseConversationResponse();
    }
}
