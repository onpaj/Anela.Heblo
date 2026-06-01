using MediatR;

namespace Anela.Heblo.Application.Features.Invoices.UseCases.GetIssuedInvoicesList;

/// <summary>
/// Request for getting paginated list of issued invoices with filtering
/// </summary>
public class GetIssuedInvoicesListRequest : IRequest<GetIssuedInvoicesListResponse>
{
    public int PageNumber { get; set; } = 1;
    public int PageSize { get; set; } = 20;
    public string? SortBy { get; set; } = "InvoiceDate";
    public bool SortDescending { get; set; } = true;

    // Filter by invoice ID (number)
    public string? InvoiceId { get; set; }

    // Filter by customer name
    public string? CustomerName { get; set; }

    // Filter by date range
    public DateTime? InvoiceDateFrom { get; set; }
    public DateTime? InvoiceDateTo { get; set; }

    // Filter by sync status
    public bool? IsSynced { get; set; }

    // Filter to show only unsynced invoices (shortcut)
    public bool ShowOnlyUnsynced { get; set; } = false;

    // Filter to show only invoices with errors
    public bool ShowOnlyWithErrors { get; set; } = false;
}