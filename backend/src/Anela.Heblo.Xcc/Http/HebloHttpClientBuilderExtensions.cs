using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Anela.Heblo.Xcc.Http;

/// <summary>
/// IHttpClientBuilder helpers that wire every Heblo outbound HttpClient with the same
/// observability + connection-pool defaults. Use WithHebloOutboundDefaults() for typical
/// registrations; use WithHebloOutboundObservability() when the caller has already
/// configured a custom primary handler.
/// </summary>
public static class HebloHttpClientBuilderExtensions
{
    /// <summary>
    /// Attaches OutboundCallObservabilityHandler as a delegating handler. Does not change
    /// the primary handler — safe to use on registrations that already call
    /// ConfigurePrimaryHttpMessageHandler with a custom handler.
    /// </summary>
    public static IHttpClientBuilder WithHebloOutboundObservability(this IHttpClientBuilder builder)
    {
        return builder.AddHttpMessageHandler<OutboundCallObservabilityHandler>();
    }

    /// <summary>
    /// Attaches the observability handler AND configures the primary handler to a
    /// SocketsHttpHandler with the configured PooledConnectionLifetime. Callers that
    /// need a non-default primary handler (HttpClientHandler, custom redirect rules,
    /// etc.) must use WithHebloOutboundObservability() instead.
    /// </summary>
    public static IHttpClientBuilder WithHebloOutboundDefaults(this IHttpClientBuilder builder)
    {
        builder.ConfigurePrimaryHttpMessageHandler(sp =>
        {
            var options = sp.GetRequiredService<IOptions<OutboundResilienceOptions>>().Value;
            return new SocketsHttpHandler
            {
                PooledConnectionLifetime = options.PooledConnectionLifetime,
            };
        });
        return builder.WithHebloOutboundObservability();
    }
}
