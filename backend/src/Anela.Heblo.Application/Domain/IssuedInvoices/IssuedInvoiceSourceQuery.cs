namespace Anela.Heblo.Application.Domain.IssuedInvoices;

public class IssuedInvoiceSourceQuery
{
    public string RequestId { get; set; } = "undefined";
    public string? InvoiceId { get; set; }

    public DateTime? DateFrom { get; set; }
    public DateTime? DateTo { get; set; }
    public string Currency { get; set; } = "CZK";

    public bool QueryByInvoice => InvoiceId != null;
    public bool QueryByDate => DateFrom != null && DateTo != null;

    public string DateFromString => DateFrom?.ToString("d.M.yyyy") ?? "";
    public string DateToString => DateTo?.ToString("d.M.yyyy") ?? "";
}



