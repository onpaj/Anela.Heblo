using Anela.Heblo.Domain.Features.Manufacture.Conditions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Anela.Heblo.Adapters.HomeAssistant;

public static class HomeAssistantAdapterServiceCollectionExtensions
{
    public static IServiceCollection AddHomeAssistantAdapter(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var section = configuration.GetSection(HomeAssistantSettings.ConfigurationKey);
        services.Configure<HomeAssistantSettings>(section);

        var settings = section.Get<HomeAssistantSettings>() ?? new HomeAssistantSettings();

        services.AddHttpClient<HomeAssistantConditionsReadingProvider>(client =>
        {
            client.BaseAddress = new Uri(settings.BaseUrl ?? "http://localhost:8123");
            client.Timeout = TimeSpan.FromSeconds(settings.RequestTimeoutSeconds);
            client.DefaultRequestHeaders.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", settings.AccessToken);
        });

        services.AddTransient<IConditionsReadingProvider>(
            sp => sp.GetRequiredService<HomeAssistantConditionsReadingProvider>());

        return services;
    }
}
