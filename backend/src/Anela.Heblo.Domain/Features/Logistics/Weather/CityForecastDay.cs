namespace Anela.Heblo.Domain.Features.Logistics.Weather;

public record CityForecastDay(DateOnly Date, double MaxTemperatureCelsius, int WeatherCode);
