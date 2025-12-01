namespace Anela.Heblo.Application.Features.Invoices.Contracts;

public class IssuedInvoiceDailyImportArgs
{
    public DateTime Day { get; set; }
    public string Currency { get; set; } = "CZK";

    public IssuedInvoiceDailyImportArgs()
    {
    }

    public IssuedInvoiceDailyImportArgs(DateTime day, string currency)
    {
        Day = day;
        Currency = currency;
    }
}