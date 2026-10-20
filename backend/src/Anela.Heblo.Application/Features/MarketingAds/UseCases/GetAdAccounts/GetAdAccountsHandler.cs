using Anela.Heblo.Application.Features.MarketingAds.Reporting;
using Anela.Heblo.Application.Shared;
using MediatR;

namespace Anela.Heblo.Application.Features.MarketingAds.UseCases.GetAdAccounts;

public class GetAdAccountsHandler : IRequestHandler<GetAdAccountsRequest, GetAdAccountsResponse>
{
    private readonly IAdsReadRepository _repository;

    public GetAdAccountsHandler(IAdsReadRepository repository) => _repository = repository;

    public async Task<GetAdAccountsResponse> Handle(GetAdAccountsRequest request, CancellationToken cancellationToken)
    {
        if (!_repository.IsAvailable)
            return new GetAdAccountsResponse(ErrorCodes.ConfigurationError, AdReadValidation.NotConfigured());

        var accounts = await _repository.ListAccountsAsync(request.Platform, request.IncludeUnmanaged, cancellationToken);
        return new GetAdAccountsResponse { Accounts = accounts.ToList() };
    }
}
