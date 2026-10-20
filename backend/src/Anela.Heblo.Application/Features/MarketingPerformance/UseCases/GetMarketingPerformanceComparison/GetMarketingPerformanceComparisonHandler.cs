using Anela.Heblo.Application.Features.MarketingPerformance.Configuration;
using Anela.Heblo.Application.Features.MarketingPerformance.Contracts;
using Anela.Heblo.Application.Features.MarketingPerformance.Services;
using Anela.Heblo.Domain.Features.MarketingPerformance;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Anela.Heblo.Application.Features.MarketingPerformance.UseCases.GetMarketingPerformanceComparison;

public class GetMarketingPerformanceComparisonHandler : IRequestHandler<GetMarketingPerformanceComparisonRequest, GetMarketingPerformanceComparisonResponse>
{
    private const int MinYears = 2;
    private const int MaxYears = 3;

    private readonly IMarketingPerformanceRepository _repository;
    private readonly MarketingPerformanceOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<GetMarketingPerformanceComparisonHandler> _logger;

    public GetMarketingPerformanceComparisonHandler(
        IMarketingPerformanceRepository repository,
        IOptions<MarketingPerformanceOptions> options,
        TimeProvider timeProvider,
        ILogger<GetMarketingPerformanceComparisonHandler> logger)
    {
        _repository = repository;
        _options = options.Value;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task<GetMarketingPerformanceComparisonResponse> Handle(GetMarketingPerformanceComparisonRequest request, CancellationToken cancellationToken)
    {
        var years = Math.Clamp(request.Years, MinYears, MaxYears);
        var current = YearMonth.From(_timeProvider.GetLocalNow().DateTime);
        var anchorYear = current.Year;
        var oldestYear = anchorYear - years + 1;

        var channels = _options.ToDefinitions();
        var calculator = new MarketingMetricsCalculator(_options.VatRate, channels);

        // Load one year further back than the oldest displayed year. That extra year is never itself
        // rendered as a series here - it exists only to supply the oldest displayed year's prior-year
        // comparison, matching the months endpoint (GetMarketingPerformanceMonthsHandler), which loads
        // the same extra year for the same reason. Without it, the two views disagree on YoY for the
        // oldest displayed year: the months/trend view shows real r/r figures while this comparison view
        // shows null (em-dash) for the same months, because it never had the prior year's rows to compare.
        var rows = await _repository.GetRangeAsync(new YearMonth(oldestYear - 1, 1), new YearMonth(anchorYear, 12), cancellationToken);
        var byKey = rows.ToDictionary(r => r.Key);

        var series = Enumerable.Range(0, years)
            .Select(i => anchorYear - i)
            .Select(year =>
            {
                var months = Enumerable.Range(1, 12).Select(m =>
                {
                    var ym = new YearMonth(year, m);
                    if (!byKey.TryGetValue(ym, out var row))
                    {
                        return calculator.Empty(ym, ym == current);
                    }
                    byKey.TryGetValue(ym.AddMonths(-12), out var lastYear);
                    return calculator.Build(row, lastYear, request.IncludeWholesale, ym == current);
                }).ToList();

                var ytd = months.Where(m => m.HasData && (year < anchorYear ? m.Month <= current.Month : true)).ToList();
                var ytdRevenue = ytd.Sum(m => m.RevenueWithoutVat);
                var ytdCost = ytd.Sum(m => m.TotalCost);
                return new MarketingYearSeriesDto
                {
                    Year = year,
                    Months = months,
                    YtdOrders = ytd.Sum(m => m.Orders),
                    YtdRevenueWithoutVat = ytdRevenue,
                    YtdTotalCost = ytdCost,
                    YtdPno = MarketingMetricsCalculator.Ratio(ytdCost * 100m, ytdRevenue),
                };
            })
            .ToList();

        _logger.LogDebug("Marketing performance comparison: {Years} years anchored at {Anchor}", years, anchorYear);

        return new GetMarketingPerformanceComparisonResponse
        {
            Series = series,
            AnchorYear = anchorYear,
            CurrentMonth = current.Month,
            Channels = channels.Select(c => new ChannelInfoDto { Code = c.Code, Label = c.Label }).ToList(),
            IncludeWholesale = request.IncludeWholesale,
            LastRefreshAt = await _repository.GetLastComputedAtAsync(cancellationToken),
        };
    }
}
