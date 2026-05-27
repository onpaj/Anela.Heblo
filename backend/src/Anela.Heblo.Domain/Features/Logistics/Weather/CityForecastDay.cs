namespace Anela.Heblo.Domain.Features.Logistics.Weather;

public record CityForecastDay(DateOnly Date, double MinTemperatureCelsius, double MaxTemperatureCelsius, int WeatherCode);
