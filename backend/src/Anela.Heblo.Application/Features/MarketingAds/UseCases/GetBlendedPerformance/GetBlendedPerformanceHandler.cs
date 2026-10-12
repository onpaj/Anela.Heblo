using Anela.Heblo.Application.Features.MarketingAds.Reporting;
using Anela.Heblo.Application.Shared;
using MediatR;

namespace Anela.Heblo.Application.Features.MarketingAds.UseCases.GetBlendedPerformance;

public class GetBlendedPerformanceHandler : IRequestHandler<GetBlendedPerformanceRequest, GetBlendedPerformanceResponse>
{
    public const int MaxRangeDays = 731;
    private readonly IAdsReadRepository _repository;

    public GetBlendedPerformanceHandler(IAdsReadRepository repository) => _repository = repository;

    public async Task<GetBlendedPerformanceResponse> Handle(GetBlendedPerformanceRequest request, CancellationToken cancellationToken)
    {
        if (!_repository.IsAvailable)
            return new GetBlendedPerformanceResponse(ErrorCodes.ConfigurationError, AdReadValidation.NotConfigured());
        if (AdReadValidation.UndefinedEnum(nameof(request.Granularity), request.Granularity) is { } enumError)
            return new GetBlendedPerformanceResponse(ErrorCodes.InvalidValue, enumError);
        if (AdReadValidation.DateRange(request.From, request.To, MaxRangeDays) is { } rangeError)
            return new GetBlendedPerformanceResponse(ErrorCodes.InvalidDateRange, rangeError);

        var rows = await _repository.GetBlendedDailyAsync(request.From, request.To, cancellationToken);
        if (rows is null)
            return new GetBlendedPerformanceResponse(ErrorCodes.ConfigurationError, AdReadValidation.NotConfigured("BlendedViewMissing"));

        return new GetBlendedPerformanceResponse
        {
            Granularity = request.Granularity,
            From = request.From,
            To = request.To,
            Periods = AdBlendedAggregator.ToPeriods(rows, request.Granularity, request.From, request.To),
            Total = AdBlendedAggregator.Total(rows, request.From),
        };
    }
}
