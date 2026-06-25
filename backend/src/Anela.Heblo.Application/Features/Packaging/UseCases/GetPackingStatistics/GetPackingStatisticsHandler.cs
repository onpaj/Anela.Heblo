using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Packaging;
using MediatR;

namespace Anela.Heblo.Application.Features.Packaging.UseCases.GetPackingStatistics;

public class GetPackingStatisticsHandler
    : IRequestHandler<GetPackingStatisticsRequest, GetPackingStatisticsResponse>
{
    private const int DefaultRangeDays = 30;

    private readonly IPackageRepository _repo;
    private readonly TimeProvider _timeProvider;

    public GetPackingStatisticsHandler(IPackageRepository repo, TimeProvider timeProvider)
    {
        _repo = repo;
        _timeProvider = timeProvider;
    }

    public async Task<GetPackingStatisticsResponse> Handle(
        GetPackingStatisticsRequest request,
        CancellationToken cancellationToken)
    {
        var localZone = TimeZoneInfo.Local;
        var today = DateOnly.FromDateTime(_timeProvider.GetLocalNow().Date);

        var toDate = request.ToDate.HasValue ? DateOnly.FromDateTime(request.ToDate.Value) : today;
        var fromDate = request.FromDate.HasValue
            ? DateOnly.FromDateTime(request.FromDate.Value)
            : toDate.AddDays(-(DefaultRangeDays - 1));

        if (fromDate > toDate)
        {
            return new GetPackingStatisticsResponse(ErrorCodes.InvalidDateRange);
        }

        // Half-open UTC window covering the local days [fromDate, toDate].
        var fromUtc = ToLocalDayStart(fromDate, localZone);
        var toUtc = ToLocalDayStart(toDate.AddDays(1), localZone);

        var stats = await _repo.GetPackingStatisticsAsync(fromUtc, toUtc, localZone, cancellationToken);

        return Map(stats, fromDate, toDate);
    }

    private static DateTimeOffset ToLocalDayStart(DateOnly day, TimeZoneInfo zone)
    {
        var dayStart = day.ToDateTime(TimeOnly.MinValue);
        return new DateTimeOffset(dayStart, zone.GetUtcOffset(dayStart));
    }

    private static GetPackingStatisticsResponse Map(PackingStatistics stats, DateOnly fromDate, DateOnly toDate)
    {
        var busiestDay = stats.ThroughputDaily
            .OrderByDescending(d => d.PackageCount)
            .FirstOrDefault();
        var busiestHour = stats.HourHeatmap
            .OrderByDescending(h => h.PackageCount)
            .FirstOrDefault();

        return new GetPackingStatisticsResponse
        {
            FromDate = fromDate.ToDateTime(TimeOnly.MinValue),
            ToDate = toDate.ToDateTime(TimeOnly.MinValue),
            PackerAttributionSince = stats.PackerAttributionSince?.ToDateTime(TimeOnly.MinValue),
            Summary = new PackingStatisticsSummaryDto
            {
                TotalPackages = stats.TotalPackages,
                TotalOrders = stats.TotalOrders,
                DistinctPackers = stats.ByPacker.Count,
                AveragePackagesPerOrder = stats.TotalOrders == 0
                    ? 0
                    : Math.Round((double)stats.TotalPackages / stats.TotalOrders, 2),
                TrackingCoveragePercent = stats.TotalPackages == 0
                    ? 0
                    : Math.Round(100.0 * stats.PackagesWithTracking / stats.TotalPackages, 1),
                BusiestDay = busiestDay is null ? null : MapDaily(busiestDay),
                BusiestHour = busiestHour is null ? null : MapHour(busiestHour),
            },
            ThroughputDaily = stats.ThroughputDaily.Select(MapDaily).ToList(),
            HourHeatmap = stats.HourHeatmap.Select(MapHour).ToList(),
            ByPacker = stats.ByPacker.Select(p => new PackerThroughputDto
            {
                PackerId = p.PackerId,
                PackerName = p.PackerName ?? "Neznámý",
                OrderCount = p.OrderCount,
                PackageCount = p.PackageCount,
            }).ToList(),
            ByCarrier = stats.ByCarrier.Select(c => new CarrierMixDto
            {
                Code = c.Code,
                Name = c.Name ?? c.Code,
                PackageCount = c.PackageCount,
            }).ToList(),
            PackagesPerOrder = stats.PackagesPerOrder.Select(b => new PackagesPerOrderBucketDto
            {
                PackageCount = b.PackageCount,
                OrderCount = b.OrderCount,
            }).ToList(),
        };
    }

    private static DailyThroughputDto MapDaily(DailyThroughput d) => new()
    {
        Date = d.Date.ToDateTime(TimeOnly.MinValue),
        OrderCount = d.OrderCount,
        PackageCount = d.PackageCount,
    };

    private static HourBucketDto MapHour(HourBucket h) => new()
    {
        DayOfWeek = h.DayOfWeek,
        Hour = h.Hour,
        PackageCount = h.PackageCount,
    };
}
