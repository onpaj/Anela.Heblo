using Anela.Heblo.Application.Features.Invoices.Contracts;
using MediatR;

namespace Anela.Heblo.Application.Features.Invoices.UseCases.EnqueueInvoiceImport;

public class EnqueueInvoiceImportRequest : IRequest<List<string>>
{
    public ImportInvoiceRequestDto ImportRequest { get; set; } = new();
}