namespace Anela.Heblo.Application.Features.Weather.Model;

public record GetWeatherForecastResponse
{
    public DateTime Date { get; init; }
    public int TemperatureC { get; init; }
    public int TemperatureF => 32 + (int)(TemperatureC / 0.5556);
    public string? Summary { get; init; }
}