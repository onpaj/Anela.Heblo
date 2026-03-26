namespace Anela.Heblo.Adapters.SendGrid;

public class SendGridOptions
{
    public const string ConfigurationKey = "SendGrid";

    public string ApiKey { get; set; } = string.Empty;
}
