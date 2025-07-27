using Anela.Heblo.Application.Features.Weather.Application;
using Anela.Heblo.Application.Features.Weather.Contracts;
using Microsoft.Extensions.DependencyInjection;

namespace Anela.Heblo.Application.Features.Weather;

public static class WeatherModule
{
    public static IServiceCollection AddWeatherModule(this IServiceCollection services)
    {
        // Register application services
        services.AddScoped<IWeatherService, WeatherService>();
        services.AddScoped<GetWeatherForecastUseCase>();
        
        return services;
    }
}