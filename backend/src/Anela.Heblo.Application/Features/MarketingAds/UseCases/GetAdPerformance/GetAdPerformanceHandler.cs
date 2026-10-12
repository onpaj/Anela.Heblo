using Anela.Heblo.Application.Features.MarketingAds.Reporting;
using Anela.Heblo.Application.Shared;
using MediatR;

namespace Anela.Heblo.Application.Features.MarketingAds.UseCases.GetAdPerformance;

public class GetAdPerformanceHandler : IRequestHandler<GetAdPerformanceRequest, GetAdPerformanceResponse>
{
    public const int MaxRangeDays = 93;
    public const int MaxPageSize = 500;
    private readonly IAdsReadRepository _repository;

    public GetAdPerformanceHandler(IAdsReadRepository repository) => _repository = repository;

    public async Task<GetAdPerformanceResponse> Handle(GetAdPerformanceRequest request, CancellationToken cancellationToken)
    {
        if (!_repository.IsAvailable)
            return new GetAdPerformanceResponse(ErrorCodes.ConfigurationError, AdReadValidation.NotConfigured());
        if (string.IsNullOrWhiteSpace(request.AccountExternalId))
            return new GetAdPerformanceResponse(ErrorCodes.RequiredFieldMissing, new() { ["Field"] = nameof(request.AccountExternalId) });
        if ((AdReadValidation.UndefinedEnum(nameof(request.Platform), request.Platform)
             ?? AdReadValidation.UndefinedEnum(nameof(request.Level), request.Level)) is { } enumError)
            return new GetAdPerformanceResponse(ErrorCodes.InvalidValue, enumError);
        if (AdReadValidation.DateRange(request.From, request.To, MaxRangeDays) is { } rangeError)
            return new GetAdPerformanceResponse(ErrorCodes.InvalidDateRange, rangeError);
        if ((AdReadValidation.Positive(nameof(request.PageNumber), request.PageNumber, AdReadValidation.MaxPageNumber)
             ?? AdReadValidation.Positive(nameof(request.PageSize), request.PageSize, MaxPageSize)) is { } pagingError)
            return new GetAdPerformanceResponse(ErrorCodes.InvalidValue, pagingError);

        var accountId = request.AccountExternalId.Trim();
        var page = await _repository.GetPerformanceAsync(new AdPerformanceQuery(request.Platform, accountId,
            request.Level, request.From, request.To, request.ParentExternalId, request.PageNumber, request.PageSize), cancellationToken);
        if (page is null)
            return new GetAdPerformanceResponse(ErrorCodes.ResourceNotFound, new() { ["Account"] = accountId });

        return new GetAdPerformanceResponse
        {
            Platform = request.Platform,
            AccountExternalId = accountId,
            Level = request.Level,
            From = request.From,
            To = request.To,
            Currency = page.Currency,
            PageNumber = request.PageNumber,
            PageSize = request.PageSize,
            TotalCount = page.TotalCount,
            Rows = page.Rows.ToList(),
        };
    }
}
