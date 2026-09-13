namespace Anela.Heblo.Application.Features.MarketingInvoices.Contracts;

public class MarketingImportResult
{
    public int Imported { get; set; }
    public int Skipped { get; set; }
    public int Failed { get; set; }
}
