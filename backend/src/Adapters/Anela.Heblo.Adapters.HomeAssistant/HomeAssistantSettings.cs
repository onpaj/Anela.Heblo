namespace Anela.Heblo.Adapters.HomeAssistant;

public class HomeAssistantSettings
{
    public static string ConfigurationKey => "HomeAssistant";

    public string BaseUrl { get; init; } = null!;
    public string AccessToken { get; init; } = null!;
    public string InnerTemperatureEntityId { get; init; } = null!;
    public string InnerHumidityEntityId { get; init; } = null!;
    public string OuterTemperatureEntityId { get; init; } = null!;
    public string OuterHumidityEntityId { get; init; } = null!;
    public int RequestTimeoutSeconds { get; init; } = 3;
    public int ConditionsCacheDurationMinutes { get; init; } = 5;
}
