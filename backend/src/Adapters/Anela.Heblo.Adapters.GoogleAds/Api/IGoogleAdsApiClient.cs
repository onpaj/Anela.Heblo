using System.Text.Json;
using System.Text.Json.Nodes;

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

    /// <summary>
    /// POST customers/{id}/{service}:mutate. Google rejecting the request (HTTP 400/404/409) comes back as a
    /// result with IsSuccess = false; auth and transport failures throw GoogleAdsApiException. Only HTTP 429
    /// is retried: any other failure may already have been applied.
    /// </summary>
    Task<GoogleAdsMutateResult> MutateAsync(string customerId, string service, JsonObject request, CancellationToken ct);
}
