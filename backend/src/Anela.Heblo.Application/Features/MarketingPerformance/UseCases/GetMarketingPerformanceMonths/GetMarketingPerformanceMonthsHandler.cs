using Anela.Heblo.Application.Features.MarketingPerformance.Configuration;
using Anela.Heblo.Application.Features.MarketingPerformance.Contracts;
using Anela.Heblo.Application.Features.MarketingPerformance.Services;
using Anela.Heblo.Domain.Features.MarketingPerformance;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Anela.Heblo.Application.Features.MarketingPerformance.UseCases.GetMarketingPerformanceMonths;

public class GetMarketingPerformanceMonthsHandler : IRequestHandler<GetMarketingPerformanceMonthsRequest, GetMarketingPerformanceMonthsResponse>
{
    private const int DefaultMonths = 36;
    private const int MaxMonths = 60;

    private readonly IMarketingPerformanceRepository _repository;
    private readonly MarketingPerformanceOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<GetMarketingPerformanceMonthsHandler> _logger;

    public GetMarketingPerformanceMonthsHandler(
        IMarketingPerformanceRepository repository,
        IOptions<MarketingPerformanceOptions> options,
        TimeProvider timeProvider,
        ILogger<GetMarketingPerformanceMonthsHandler> logger)
    {
        _repository = repository;
        _options = options.Value;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task<GetMarketingPerformanceMonthsResponse> Handle(GetMarketingPerformanceMonthsRequest request, CancellationToken cancellationToken)
    {
        var current = YearMonth.From(_timeProvider.GetLocalNow().DateTime);
        var from = request.From ?? current.AddMonths(-(DefaultMonths - 1)).ToString();
        var to = request.To ?? current.ToString();

        var range = MonthRangeParser.Parse(from, to, current, MaxMonths);
        if (!range.IsValid)
        {
            return new GetMarketingPerformanceMonthsResponse(range.Error!.Value, range.Params);
        }

        var channels = _options.ToDefinitions();
        var calculator = new MarketingMetricsCalculator(_options.VatRate, channels);

        // Load one extra year back so YoY ratios have their prior-year cell.
        var rows = await _repository.GetRangeAsync(range.From.AddMonths(-12), range.To, cancellationToken);
        var byKey = rows.ToDictionary(r => r.Key);

        var months = YearMonth.Range(range.From, range.To).Select(ym =>
        {
            var isPartial = ym == current;
            if (!byKey.TryGetValue(ym, out var row))
            {
                return calculator.Empty(ym, isPartial);
            }
            byKey.TryGetValue(ym.AddMonths(-12), out var lastYear);
            return calculator.Build(row, lastYear, request.IncludeWholesale, isPartial);
        }).ToList();

        _logger.LogDebug("Marketing performance months {From}..{To}: {Count} rows, {WithData} with data", range.From, range.To, months.Count, months.Count(m => m.HasData));

        return new GetMarketingPerformanceMonthsResponse
        {
            Months = months,
            Channels = channels.Select(c => new ChannelInfoDto { Code = c.Code, Label = c.Label }).ToList(),
            From = range.From.ToString(),
            To = range.To.ToString(),
            IncludeWholesale = request.IncludeWholesale,
            VatRate = _options.VatRate,
            LastRefreshAt = await _repository.GetLastComputedAtAsync(cancellationToken),
        };
    }
}
