namespace Anela.Heblo.Adapters.Comgate;

public class ComgateSettings
{
    public static string ConfigurationKey { get; set; } = "Comgate";

    public required string MerchantId { get; set; }
    public required string Secret { get; set; }
}