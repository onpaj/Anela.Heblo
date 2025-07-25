namespace Anela.Heblo.API;

public class WeatherForecast
{
    public DateOnly Date { get; set; }

    public int TemperatureC { get; set; }

    public int TemperatureF => 32 + (int)(TemperatureC * 9.0 / 5.0);

    public string? Summary { get; set; }
}