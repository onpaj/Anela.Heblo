using Google.Analytics.Data.V1Beta;
using Google.Apis.Auth.OAuth2;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Anela.Heblo.Adapters.GoogleAnalytics;

/// <summary>
/// Thin wrapper over <see cref="BetaAnalyticsDataClient"/>. Everything SDK-shaped stops here:
/// the sync services see <see cref="Ga4ReportRequest"/> / <see cref="Ga4Row"/> only.
/// </summary>
public sealed class Ga4ReportClient : IGa4ReportClient
{
    private readonly BetaAnalyticsDataClient _client;
    private readonly string _property;
    private readonly Ga4SyncOptions _syncOptions;
    private readonly ILogger<Ga4ReportClient> _logger;

    public Ga4ReportClient(
        IOptions<Ga4Options> options,
        IOptions<Ga4SyncOptions> syncOptions,
        ILogger<Ga4ReportClient> logger)
    {
        var value = options.Value;
        _syncOptions = syncOptions.Value;
        _logger = logger;
        _property = $"properties/{value.PropertyId}";

        // CredentialFactory rather than the deprecated GoogleCredential.FromJson(string).
        var credential = CredentialFactory
            .FromJson<ServiceAccountCredential>(value.CredentialsJson)
            .ToGoogleCredential()
            .CreateScoped("https://www.googleapis.com/auth/analytics.readonly");

        _client = new BetaAnalyticsDataClientBuilder { GoogleCredential = credential }.Build();
    }

    public async Task<Ga4ReportResult> RunReportAsync(Ga4ReportRequest request, CancellationToken ct = default)
    {
        var rows = new List<Ga4Row>();
        Ga4QuotaSnapshot? quota = null;
        var dataLoss = false;
        var thresholded = false;

        var offset = 0L;
        while (true)
        {
            ct.ThrowIfCancellationRequested();

            var apiRequest = BuildRequest(request, offset);
            var response = await _client.RunReportAsync(apiRequest, ct);

            foreach (var row in response.Rows)
            {
                rows.Add(new Ga4Row(
                    row.DimensionValues.Select(v => v.Value ?? string.Empty).ToArray(),
                    row.MetricValues.Select(v => v.Value ?? string.Empty).ToArray()));
            }

            quota = ReadQuota(response) ?? quota;
            dataLoss |= response.Metadata?.DataLossFromOtherRow ?? false;
            thresholded |= response.Metadata?.SubjectToThresholding ?? false;

            offset += response.Rows.Count;
            if (response.Rows.Count == 0 || offset >= response.RowCount)
                break;

            await ThrottleAsync(ct);
        }

        if (quota != null)
        {
            _logger.LogInformation(
                "Ga4.QuotaAfterReport tokensPerDayRemaining={TokensPerDay} tokensPerHourRemaining={TokensPerHour} concurrentRemaining={Concurrent}",
                quota.TokensPerDayRemaining, quota.TokensPerHourRemaining, quota.ConcurrentRequestsRemaining);
        }

        if (dataLoss)
        {
            _logger.LogWarning(
                "Ga4.DataLossFromOtherRow dimensions={Dimensions} start={Start} end={End} — GA4 bucketed some rows into (other); the stored totals for this window are incomplete.",
                string.Join(",", request.Dimensions), request.StartDate, request.EndDate);
        }

        return new Ga4ReportResult(rows, quota, dataLoss, thresholded);
    }

    private RunReportRequest BuildRequest(Ga4ReportRequest request, long offset)
    {
        var apiRequest = new RunReportRequest
        {
            Property = _property,
            DateRanges =
            {
                new DateRange
                {
                    StartDate = request.StartDate.ToString("yyyy-MM-dd"),
                    EndDate = request.EndDate.ToString("yyyy-MM-dd"),
                },
            },
            Limit = _syncOptions.PageSize,
            Offset = offset,
            ReturnPropertyQuota = true,
            // GA4 hides "(other)" bucketing behind an aggregate row by default; asking for the
            // total lets us see the rows we did get versus what the property actually recorded.
            KeepEmptyRows = false,
        };

        foreach (var dimension in request.Dimensions)
            apiRequest.Dimensions.Add(new Dimension { Name = dimension });

        foreach (var metric in request.Metrics)
            apiRequest.Metrics.Add(new Metric { Name = metric });

        var filter = BuildPrefixFilter(request);
        if (filter != null)
            apiRequest.DimensionFilter = filter;

        return apiRequest;
    }

    /// <summary>
    /// An OR-group of BEGINS_WITH filters, so a path-prefix restriction runs inside GA4 rather
    /// than pulling the whole catalogue over the wire and discarding it here.
    /// </summary>
    private static FilterExpression? BuildPrefixFilter(Ga4ReportRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.FilterDimension) || request.FilterBeginsWithAny.Count == 0)
            return null;

        var orGroup = new FilterExpressionList();
        foreach (var prefix in request.FilterBeginsWithAny)
        {
            orGroup.Expressions.Add(new FilterExpression
            {
                Filter = new Filter
                {
                    FieldName = request.FilterDimension,
                    StringFilter = new Filter.Types.StringFilter
                    {
                        MatchType = Filter.Types.StringFilter.Types.MatchType.BeginsWith,
                        Value = prefix,
                        CaseSensitive = false,
                    },
                },
            });
        }

        return new FilterExpression { OrGroup = orGroup };
    }

    private static Ga4QuotaSnapshot? ReadQuota(RunReportResponse response)
    {
        var quota = response.PropertyQuota;
        if (quota == null)
            return null;

        return new Ga4QuotaSnapshot(
            quota.TokensPerDay?.Remaining ?? -1,
            quota.TokensPerHour?.Remaining ?? -1,
            quota.ConcurrentRequests?.Remaining ?? -1);
    }

    private Task ThrottleAsync(CancellationToken ct) =>
        _syncOptions.ThrottleMilliseconds > 0
            ? Task.Delay(_syncOptions.ThrottleMilliseconds, ct)
            : Task.CompletedTask;
}
