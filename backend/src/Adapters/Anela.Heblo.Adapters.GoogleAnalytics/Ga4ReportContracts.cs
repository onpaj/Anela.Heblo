namespace Anela.Heblo.Adapters.GoogleAnalytics;

/// <summary>
/// A single runReport call, expressed without any reference to the Google SDK so the sync
/// services can be tested against a fake.
/// </summary>
public sealed class Ga4ReportRequest
{
    public IReadOnlyList<string> Dimensions { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> Metrics { get; init; } = Array.Empty<string>();
    public DateOnly StartDate { get; init; }
    public DateOnly EndDate { get; init; }

    /// <summary>Dimension the <see cref="FilterBeginsWithAny"/> prefixes apply to, e.g. "pagePath".</summary>
    public string? FilterDimension { get; init; }

    /// <summary>Keep only rows whose <see cref="FilterDimension"/> begins with one of these. Empty means no filter.</summary>
    public IReadOnlyList<string> FilterBeginsWithAny { get; init; } = Array.Empty<string>();
}

/// <summary>One report row: dimension values then metric values, in the order they were requested.</summary>
public sealed record Ga4Row(IReadOnlyList<string> DimensionValues, IReadOnlyList<string> MetricValues);

/// <summary>
/// What the Data API said about its own quota. Logged after every call, so a backfill that is
/// about to run out of tokens says so before it fails.
/// </summary>
public sealed record Ga4QuotaSnapshot(
    int TokensPerDayRemaining,
    int TokensPerHourRemaining,
    int ConcurrentRequestsRemaining);

/// <param name="DataLossFromOtherRow">GA4 dropped rows into an "(other)" bucket because the cardinality exceeded its limit.</param>
/// <param name="SubjectToThresholding">GA4 withheld rows for privacy thresholding — a real cause of Data-API-vs-BigQuery gaps.</param>
public sealed record Ga4ReportResult(
    IReadOnlyList<Ga4Row> Rows,
    Ga4QuotaSnapshot? Quota,
    bool DataLossFromOtherRow,
    bool SubjectToThresholding);

public interface IGa4ReportClient
{
    /// <summary>Runs one report, following pagination until every row has been read.</summary>
    Task<Ga4ReportResult> RunReportAsync(Ga4ReportRequest request, CancellationToken ct = default);
}
