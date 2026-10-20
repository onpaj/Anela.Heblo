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

        if ((AdReadValidation.UndefinedEnum(nameof(request.Platform), request.Platform)
             ?? AdReadValidation.UndefinedEnum(nameof(request.Origin), request.Origin)
             ?? AdReadValidation.UndefinedEnum(nameof(request.ActorKind), request.ActorKind)) is { } enumError)
            return new GetAdChangeHistoryResponse(ErrorCodes.InvalidValue, enumError);

        var to = request.To ?? _time.GetUtcNow();
        var from = request.From ?? to.AddDays(-DefaultDays);
        if (from > to || to - from > TimeSpan.FromDays(MaxRangeDays))
            return new GetAdChangeHistoryResponse(ErrorCodes.InvalidDateRange, new() { ["MaxDays"] = MaxRangeDays.ToString() });
        if ((AdReadValidation.Positive(nameof(request.PageNumber), request.PageNumber, AdReadValidation.MaxPageNumber)
             ?? AdReadValidation.Positive(nameof(request.PageSize), request.PageSize, MaxPageSize)) is { } pagingError)
            return new GetAdChangeHistoryResponse(ErrorCodes.InvalidValue, pagingError);

        var accountId = string.IsNullOrWhiteSpace(request.AccountExternalId) ? null : request.AccountExternalId.Trim();
        var page = await _repository.GetChangeEventsAsync(new AdChangeEventQuery(request.Platform, accountId,
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
