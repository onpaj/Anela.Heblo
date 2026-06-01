namespace Anela.Heblo.Adapters.Cups;

public class CupsOptions
{
    public const string ConfigurationKey = "Cups";

    public string ServerUrl { get; set; } = string.Empty;    // e.g. "http://cups.internal:631"
    public string PrinterName { get; set; } = string.Empty;  // fallback printer name
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
}
