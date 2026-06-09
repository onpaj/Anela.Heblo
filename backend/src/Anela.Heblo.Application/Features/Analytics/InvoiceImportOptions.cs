namespace Anela.Heblo.Application.Features.Analytics;

public class InvoiceImportOptions
{
    public const string ConfigurationKey = "InvoiceImport";

    public int MinimumDailyThreshold { get; set; } = 10;
    public int DefaultDaysBack { get; set; } = 14;
}
