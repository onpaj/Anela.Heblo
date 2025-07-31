using Microsoft.Extensions.DependencyInjection;

namespace Anela.Heblo.Application.Features.Weather;

public static class WeatherModule
{
    public static IServiceCollection AddWeatherModule(this IServiceCollection services)
    {
        // MediatR handlers are automatically registered by MediatR assembly scanning
        // No manual registration needed for handlers

        return services;
    }
}