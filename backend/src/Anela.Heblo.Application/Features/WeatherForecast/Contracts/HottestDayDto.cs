namespace Anela.Heblo.Application.Features.WeatherForecast.Contracts;

public class HottestDayDto
{
    public DateOnly Date { get; set; }
    public string CityName { get; set; } = string.Empty;
    public double MinTemperatureCelsius { get; set; }
    public double MaxTemperatureCelsius { get; set; }
    public int WeatherCode { get; set; }
}
