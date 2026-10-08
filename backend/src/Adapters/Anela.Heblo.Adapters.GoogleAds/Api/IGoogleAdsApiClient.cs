using System.Text.Json;

namespace Anela.Heblo.Adapters.GoogleAds.Api;

/// <summary>
/// The only seam between Heblo and Google Ads. Tests replace it with recorded JSON fixtures
/// (read side) or a stateful fake (write side).
/// </summary>
internal interface IGoogleAdsApiClient
{
    /// <summary>
    /// Runs a GAQL query over every page and returns the <c>results</c> rows as detached elements.
    /// Throws <see cref="GoogleAdsApiException"/> on any non-2xx after retrying transient failures.
    /// </summary>
    Task<IReadOnlyList<JsonElement>> SearchAsync(string customerId, GoogleAdsQuery query, CancellationToken ct);
}
