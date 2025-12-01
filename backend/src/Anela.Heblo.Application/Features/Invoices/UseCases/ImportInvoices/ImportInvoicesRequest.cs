using Anela.Heblo.Application.Features.Invoices.Contracts;
using Anela.Heblo.Domain.Features.Invoices;
using MediatR;

namespace Anela.Heblo.Application.Features.Invoices.UseCases.ImportInvoices;

public class ImportInvoicesRequest : IRequest<ImportResultDto>
{
    public IssuedInvoiceSourceQuery Query { get; set; } = new();
}