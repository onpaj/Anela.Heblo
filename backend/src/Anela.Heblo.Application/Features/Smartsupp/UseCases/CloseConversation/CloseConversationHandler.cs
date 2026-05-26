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

    public Task<CloseConversationResponse> Handle(
        CloseConversationRequest request,
        CancellationToken cancellationToken)
        => throw new NotImplementedException();
}
