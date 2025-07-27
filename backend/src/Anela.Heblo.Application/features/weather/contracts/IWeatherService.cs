namespace Anela.Heblo.Application.Features.Weather.Contracts;

public interface IWeatherService
{
    Task<IEnumerable<GetWeatherForecastResponse>> GetForecastAsync(CancellationToken cancellationToken = default);
}