namespace Anela.Heblo.Domain.Features.Logistics.Weather;

public interface IWeatherForecastClient
{
    Task<IReadOnlyList<CityForecast>> GetForecastAsync(CancellationToken cancellationToken);
}
