using Anela.Heblo.Application.Features.WeatherForecast.DashboardTiles;
using Anela.Heblo.Xcc.Services.Dashboard;
using Microsoft.Extensions.DependencyInjection;

namespace Anela.Heblo.Application.Features.WeatherForecast;

public static class WeatherForecastModule
{
    public static IServiceCollection AddWeatherForecastModule(this IServiceCollection services)
    {
        services.RegisterTile<WeatherForecastTile>();
        return services;
    }
}
