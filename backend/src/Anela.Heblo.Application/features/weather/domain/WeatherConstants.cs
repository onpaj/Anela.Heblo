namespace Anela.Heblo.Application.Features.Weather.Domain;

public static class WeatherConstants
{
    public const int FORECAST_DAYS = 5;
    public const int MIN_TEMPERATURE = -20;
    public const int MAX_TEMPERATURE = 55;
    
    public static readonly string[] WEATHER_SUMMARIES = new[]
    {
        "Freezing", "Bracing", "Chilly", "Cool", "Mild", "Warm", "Balmy", "Hot", "Sweltering", "Scorching"
    };
}