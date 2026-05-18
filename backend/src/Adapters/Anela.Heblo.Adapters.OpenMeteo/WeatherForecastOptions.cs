namespace Anela.Heblo.Adapters.OpenMeteo;

public class WeatherForecastOptions
{
    public static string ConfigKey => "WeatherForecast";

    public List<WeatherCity> Cities { get; init; } = new();
    public int CacheDurationMinutes { get; init; } = 180;
    public int RequestTimeoutSeconds { get; init; } = 5;
}

public class WeatherCity
{
    public string Name { get; init; } = string.Empty;
    public double Latitude { get; init; }
    public double Longitude { get; init; }
}
