namespace Anela.Heblo.Domain.Features.Invoices;

public class IssuedInvoiceFilters
{
    public int PageNumber { get; set; } = 1;
    public int PageSize { get; set; } = 20;
    public string? SortBy { get; set; }
    public bool SortDescending { get; set; } = true;
    public string? InvoiceId { get; set; }
    public string? CustomerName { get; set; }
    public DateTime? InvoiceDateFrom { get; set; }
    public DateTime? InvoiceDateTo { get; set; }
    public bool? IsSynced { get; set; }
    public bool ShowOnlyUnsynced { get; set; }
    public bool ShowOnlyWithErrors { get; set; }
}
