namespace Anela.Heblo.Adapters.HomeAssistant;

public class HomeAssistantSettings
{
    public static string ConfigurationKey => "HomeAssistant";

    public string BaseUrl { get; set; } = null!;
    public string AccessToken { get; set; } = null!;
    public string InnerTemperatureEntityId { get; set; } = null!;
    public string InnerHumidityEntityId { get; set; } = null!;
    public string OuterTemperatureEntityId { get; set; } = null!;
    public string OuterHumidityEntityId { get; set; } = null!;
    public int RequestTimeoutSeconds { get; set; } = 3;
}
