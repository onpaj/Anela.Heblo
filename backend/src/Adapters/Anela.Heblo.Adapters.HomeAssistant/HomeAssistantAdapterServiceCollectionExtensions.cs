using Anela.Heblo.Domain.Features.Manufacture.Conditions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Anela.Heblo.Adapters.HomeAssistant;

public static class HomeAssistantAdapterServiceCollectionExtensions
{
    public static IServiceCollection AddHomeAssistantAdapter(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddOptions<HomeAssistantSettings>()
            .Bind(configuration.GetSection(HomeAssistantSettings.ConfigurationKey));

        services.AddMemoryCache();

        services.AddHttpClient<HomeAssistantConditionsReadingProvider>((sp, client) =>
        {
            var settings = sp.GetRequiredService<IOptions<HomeAssistantSettings>>().Value;

            if (string.IsNullOrWhiteSpace(settings.BaseUrl)
                || !Uri.TryCreate(settings.BaseUrl, UriKind.Absolute, out var baseUri))
            {
                // HomeAssistant is not configured for this environment.
                // HTTP calls in HomeAssistantConditionsReadingProvider will fail per-sensor and
                // return null, which bubbles up as ConditionsReadingSource.Unavailable — no exception.
                return;
            }

            client.BaseAddress = baseUri;
            client.Timeout = TimeSpan.FromSeconds(settings.RequestTimeoutSeconds);
            client.DefaultRequestHeaders.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", settings.AccessToken);
        });

        services.AddTransient<IConditionsReadingProvider>(
            sp => sp.GetRequiredService<HomeAssistantConditionsReadingProvider>());

        return services;
    }
}
