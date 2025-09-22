using Anela.Heblo.Application.Features.Invoice.Contracts;
using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.Invoice.UseCases.SearchInvoices;

public class SearchInvoicesResponse : BaseResponse
{
    public List<InvoiceDto> Invoices { get; set; } = new();
    public string SearchTerm { get; set; } = null!;
    public int ResultCount { get; set; }
}