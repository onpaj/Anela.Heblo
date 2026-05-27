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
        // Map only true availability problems (5xx, network, timeout, client-side cancellation)
        // to "unavailable". A 4xx from Smartsupp indicates a contract bug on our side and must
        // surface as a real failure so it does not hide behind a benign "service unavailable" toast.
        catch (Exception ex) when (IsUnavailable(ex, cancellationToken))
        {
            _logger.LogWarning(ex, "Smartsupp API unavailable while closing conversation {ConversationId}",
                request.ConversationId);
            return new CloseConversationResponse(ErrorCodes.SmartsuppCloseConversationUnavailable);
        }

        return new CloseConversationResponse();
    }

    private static bool IsUnavailable(Exception ex, CancellationToken cancellationToken) =>
        ex switch
        {
            HttpRequestException http => http.StatusCode is null || (int)http.StatusCode >= 500,
            TimeoutException => true,
            ObjectDisposedException => true,
            TaskCanceledException tce => tce.CancellationToken != cancellationToken,
            _ => false,
        };
}
