namespace Anela.Heblo.Adapters.Comgate;

public class ComgateSettings
{
    public static string ConfigurationKey { get; set; } = "Comgate";

    public string MerchantId { get; set; }
    public string Secret { get; set; }
}