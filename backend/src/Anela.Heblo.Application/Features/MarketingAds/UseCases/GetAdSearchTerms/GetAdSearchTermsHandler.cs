using Anela.Heblo.Application.Features.MarketingAds.Reporting;
using Anela.Heblo.Application.Shared;
using MediatR;

namespace Anela.Heblo.Application.Features.MarketingAds.UseCases.GetAdSearchTerms;

public class GetAdSearchTermsHandler : IRequestHandler<GetAdSearchTermsRequest, GetAdSearchTermsResponse>
{
    public const int MaxRangeDays = 93;
    public const int MaxTop = 1000;
    private readonly IAdsReadRepository _repository;

    public GetAdSearchTermsHandler(IAdsReadRepository repository) => _repository = repository;

    public async Task<GetAdSearchTermsResponse> Handle(GetAdSearchTermsRequest request, CancellationToken cancellationToken)
    {
        if (!_repository.IsAvailable)
            return new GetAdSearchTermsResponse(ErrorCodes.ConfigurationError, AdReadValidation.NotConfigured());
        if (string.IsNullOrWhiteSpace(request.AccountExternalId))
            return new GetAdSearchTermsResponse(ErrorCodes.RequiredFieldMissing, new() { ["Field"] = nameof(request.AccountExternalId) });
        if (AdReadValidation.DateRange(request.From, request.To, MaxRangeDays) is { } rangeError)
            return new GetAdSearchTermsResponse(ErrorCodes.InvalidDateRange, rangeError);
        if (AdReadValidation.Positive(nameof(request.Top), request.Top, MaxTop) is { } topError)
            return new GetAdSearchTermsResponse(ErrorCodes.InvalidValue, topError);

        var terms = await _repository.GetSearchTermsAsync(new AdSearchTermQuery(request.Platform, request.AccountExternalId.Trim(),
            request.CampaignExternalId, request.AdGroupExternalId, request.From, request.To, request.MinCost, request.Top), cancellationToken);
        if (terms is null)
            return new GetAdSearchTermsResponse(ErrorCodes.ResourceNotFound, new() { ["Account"] = request.AccountExternalId });

        return new GetAdSearchTermsResponse { From = request.From, To = request.To, Terms = terms.ToList() };
    }
}
