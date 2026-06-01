namespace Anela.Heblo.Adapters.WebSearch;

public class WebSearchAdapterOptions
{
    public string Provider { get; set; } = "Mock";
    public string ApiKey { get; set; } = "";
    public string Endpoint { get; set; } = "https://serpapi.com/search.json";
    public string DefaultLocale { get; set; } = "cs";
    public string DefaultGeo { get; set; } = "cz";
    public int TimeoutSeconds { get; set; } = 15;
}
