using Anela.Heblo.Domain.Features.Manufacture.Conditions;
using Anela.Heblo.Xcc.Http;
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
            client.BaseAddress = new Uri(
                settings.BaseUrl ?? throw new InvalidOperationException("HomeAssistant:BaseUrl is required but not configured."));
            client.Timeout = TimeSpan.FromSeconds(settings.RequestTimeoutSeconds);
            client.DefaultRequestHeaders.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", settings.AccessToken);
        }).WithHebloOutboundDefaults();

        services.AddTransient<IConditionsReadingProvider>(
            sp => sp.GetRequiredService<HomeAssistantConditionsReadingProvider>());

        return services;
    }
}
