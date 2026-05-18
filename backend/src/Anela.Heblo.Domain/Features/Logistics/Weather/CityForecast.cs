namespace Anela.Heblo.Domain.Features.Logistics.Weather;

public record CityForecast(string CityName, IReadOnlyList<CityForecastDay> Days);
