using Anela.Heblo.Application.Features.MarketingAds.Reporting;
using Anela.Heblo.Application.Shared;
using MediatR;

namespace Anela.Heblo.Application.Features.MarketingAds.UseCases.GetAdChangeHistory;

public class GetAdChangeHistoryHandler : IRequestHandler<GetAdChangeHistoryRequest, GetAdChangeHistoryResponse>
{
    public const int DefaultDays = 30;
    public const int MaxRangeDays = 92;
    public const int MaxPageSize = 500;
    private readonly IAdsReadRepository _repository;
    private readonly TimeProvider _time;

    public GetAdChangeHistoryHandler(IAdsReadRepository repository, TimeProvider time)
    {
        _repository = repository;
        _time = time;
    }

    public async Task<GetAdChangeHistoryResponse> Handle(GetAdChangeHistoryRequest request, CancellationToken cancellationToken)
    {
        if (!_repository.IsAvailable)
            return new GetAdChangeHistoryResponse(ErrorCodes.ConfigurationError, AdReadValidation.NotConfigured());

        var to = request.To ?? _time.GetUtcNow();
        var from = request.From ?? to.AddDays(-DefaultDays);
        if (from > to || to - from > TimeSpan.FromDays(MaxRangeDays))
            return new GetAdChangeHistoryResponse(ErrorCodes.InvalidDateRange, new() { ["MaxDays"] = MaxRangeDays.ToString() });
        if ((AdReadValidation.Positive(nameof(request.PageNumber), request.PageNumber, int.MaxValue)
             ?? AdReadValidation.Positive(nameof(request.PageSize), request.PageSize, MaxPageSize)) is { } pagingError)
            return new GetAdChangeHistoryResponse(ErrorCodes.InvalidValue, pagingError);

        var page = await _repository.GetChangeEventsAsync(new AdChangeEventQuery(request.Platform, request.AccountExternalId,
            request.Origin, request.ActorKind, from, to, request.PageNumber, request.PageSize), cancellationToken);

        return new GetAdChangeHistoryResponse
        {
            From = from,
            To = to,
            PageNumber = request.PageNumber,
            PageSize = request.PageSize,
            TotalCount = page.TotalCount,
            Events = page.Rows.ToList(),
        };
    }
}
