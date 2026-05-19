using Anela.Heblo.Domain.Features.Logistics.Weather;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Anela.Heblo.Adapters.OpenMeteo;

public static class HebloOpenMeteoAdapterModule
{
    public static IServiceCollection AddOpenMeteoAdapter(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddOptions<WeatherForecastOptions>()
            .Bind(configuration.GetSection(WeatherForecastOptions.ConfigKey))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddMemoryCache();

        services.AddHttpClient<OpenMeteoWeatherForecastClient>((sp, client) =>
        {
            var settings = sp.GetRequiredService<IOptions<WeatherForecastOptions>>().Value;
            client.BaseAddress = new Uri("https://api.open-meteo.com");
            client.Timeout = TimeSpan.FromSeconds(settings.RequestTimeoutSeconds);
        });

        services.AddTransient<IWeatherForecastClient>(
            sp => sp.GetRequiredService<OpenMeteoWeatherForecastClient>());

        return services;
    }
}
