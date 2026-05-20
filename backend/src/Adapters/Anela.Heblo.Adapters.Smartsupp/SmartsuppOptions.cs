namespace Anela.Heblo.Adapters.Smartsupp;

public class SmartsuppOptions
{
    public const string SectionKey = "Smartsupp";

    public string ApiToken { get; set; } = "";
    public string BaseUrl { get; set; } = "https://api.smartsupp.com/v2/";
    public int HttpTimeoutSeconds { get; set; } = 30;
    public string WebhookSecret { get; set; } = "";
    public string? WebhookAppId { get; set; }
    public List<string> IgnoredEventTypes { get; set; } = new();
}
