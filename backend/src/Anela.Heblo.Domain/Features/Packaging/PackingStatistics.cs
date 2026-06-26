namespace Anela.Heblo.Domain.Features.Packaging;

/// <summary>
/// Aggregated, read-only view of packing activity over a date window, derived
/// solely from persisted <see cref="Package"/> rows. All day/hour grouping is in
/// the local timezone passed to the aggregation so peaks line up with the workday.
/// </summary>
public sealed record PackingStatistics(
    int TotalPackages,
    int TotalOrders,
    int PackagesWithTracking,
    DateOnly? PackerAttributionSince,
    IReadOnlyList<DailyThroughput> ThroughputDaily,
    IReadOnlyList<HourBucket> HourHeatmap,
    IReadOnlyList<PackerThroughput> ByPacker,
    IReadOnlyList<CarrierThroughput> ByCarrier,
    IReadOnlyList<PackagesPerOrderBucket> PackagesPerOrder);

/// <summary>Orders and packages packed on a given local calendar day.</summary>
public sealed record DailyThroughput(DateOnly Date, int OrderCount, int PackageCount);

/// <summary>Packages packed in a given local weekday/hour cell. <paramref name="DayOfWeek"/> is ISO (1=Mon..7=Sun).</summary>
public sealed record HourBucket(int DayOfWeek, int Hour, int PackageCount);

/// <summary>Distinct orders and packages attributed to a packer. Null packer id means the row predates attribution.</summary>
public sealed record PackerThroughput(Guid? PackerId, string? PackerName, int OrderCount, int PackageCount);

/// <summary>Packages handled by a shipping provider.</summary>
public sealed record CarrierThroughput(string Code, string? Name, int PackageCount);

/// <summary>How many orders shipped in N packages. <paramref name="PackageCount"/> is capped at 3 (meaning "3 or more").</summary>
public sealed record PackagesPerOrderBucket(int PackageCount, int OrderCount);
