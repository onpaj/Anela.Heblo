using Anela.Heblo.Domain.Entities;

namespace Anela.Heblo.Application.Interfaces;

public interface IWeatherService
{
    Task<IEnumerable<WeatherForecast>> GetForecastAsync();
}