using MediatR;

namespace Anela.Heblo.Application.Features.Invoice.UseCases.SearchInvoices;

public class SearchInvoicesRequest : IRequest<SearchInvoicesResponse>
{
    public string SearchTerm { get; set; } = null!;
}