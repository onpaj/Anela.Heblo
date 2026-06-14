using System.Diagnostics;

namespace Anela.Heblo.Adapters.HomeAssistant.Resilience;

/// <summary>
/// Tags the per-attempt Activity when the HTTP send throws, so the
/// Application Insights dependency processor can drop transient retries
/// before they reach the AI ingestion endpoint.
/// </summary>
public sealed class HomeAssistantRetryActivityTaggingHandler : DelegatingHandler
{
    public const string SuppressTagName = "ha.retry-suppress";

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await base.SendAsync(request, cancellationToken);
            if (HomeAssistantTransientErrorPredicate.IsTransient(exception: null, result: response))
            {
                Activity.Current?.SetTag(SuppressTagName, "true");
            }
            return response;
        }
        catch (Exception ex) when (HomeAssistantTransientErrorPredicate.IsTransient(ex, result: null))
        {
            Activity.Current?.SetTag(SuppressTagName, "true");
            throw;
        }
    }
}
