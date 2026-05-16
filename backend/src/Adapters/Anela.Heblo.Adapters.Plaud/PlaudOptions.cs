namespace Anela.Heblo.Adapters.Plaud;

public class PlaudOptions
{
    public const string SectionKey = "Plaud";
    public string CliExecutablePath { get; set; } = "plaud";
    public string TokensJson { get; set; } = string.Empty;
    public int ProcessTimeoutSeconds { get; set; } = 60;
    public int MaxRecordingAgeDays { get; set; } = 7;
}
