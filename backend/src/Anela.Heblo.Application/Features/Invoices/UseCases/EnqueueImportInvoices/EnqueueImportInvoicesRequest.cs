using Anela.Heblo.Domain.Features.Invoices;
using MediatR;

namespace Anela.Heblo.Application.Features.Invoices.UseCases.EnqueueImportInvoices;

public class EnqueueImportInvoicesRequest : IRequest<EnqueueImportInvoicesResponse>
{
    public IssuedInvoiceSourceQuery Query { get; set; } = new();
}