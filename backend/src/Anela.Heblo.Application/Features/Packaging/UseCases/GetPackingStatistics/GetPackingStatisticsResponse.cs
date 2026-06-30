using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.Packaging.UseCases.GetPackingStatistics;

public class GetPackingStatisticsResponse : BaseResponse
{
    /// <summary>Start of the resolved local-day window (echoed so the client can render the range).</summary>
    public DateTime FromDate { get; set; }

    /// <summary>End of the resolved local-day window (inclusive).</summary>
    public DateTime ToDate { get; set; }

    /// <summary>
    /// Earliest local day with packer attribution. When later than <see cref="FromDate"/>,
    /// the packer panel only reflects part of the window — the client shows a hint.
    /// </summary>
    public DateTime? PackerAttributionSince { get; set; }

    public PackingStatisticsSummaryDto Summary { get; set; } = new();
    public List<DailyThroughputDto> ThroughputDaily { get; set; } = new();
    public List<HourBucketDto> HourHeatmap { get; set; } = new();
    public List<PackerThroughputDto> ByPacker { get; set; } = new();
    public List<CarrierMixDto> ByCarrier { get; set; } = new();
    public List<PackagesPerOrderBucketDto> PackagesPerOrder { get; set; } = new();

    public GetPackingStatisticsResponse() { }
    public GetPackingStatisticsResponse(ErrorCodes errorCode) : base(errorCode) { }
}

public class PackingStatisticsSummaryDto
{
    public int TotalPackages { get; set; }
    public int TotalOrders { get; set; }
    public int DistinctPackers { get; set; }
    public double AveragePackagesPerOrder { get; set; }
    public double TrackingCoveragePercent { get; set; }

    /// <summary>Local day with the most packages, if any.</summary>
    public DailyThroughputDto? BusiestDay { get; set; }

    /// <summary>Weekday/hour cell with the most packages, if any.</summary>
    public HourBucketDto? BusiestHour { get; set; }
}

public class DailyThroughputDto
{
    public DateTime Date { get; set; }
    public int OrderCount { get; set; }
    public int PackageCount { get; set; }
}

public class HourBucketDto
{
    /// <summary>ISO weekday: 1 = Monday .. 7 = Sunday.</summary>
    public int DayOfWeek { get; set; }
    public int Hour { get; set; }
    public int PackageCount { get; set; }
}

public class PackerThroughputDto
{
    public Guid? PackerId { get; set; }
    public string PackerName { get; set; } = string.Empty;
    public int OrderCount { get; set; }
    public int PackageCount { get; set; }
}

public class CarrierMixDto
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public int PackageCount { get; set; }
}

public class PackagesPerOrderBucketDto
{
    /// <summary>Number of packages per order; 3 means "3 or more".</summary>
    public int PackageCount { get; set; }
    public int OrderCount { get; set; }
}
