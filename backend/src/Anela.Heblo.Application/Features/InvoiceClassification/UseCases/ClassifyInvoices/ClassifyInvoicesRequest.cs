using MediatR;

namespace Anela.Heblo.Application.Features.InvoiceClassification.UseCases.ClassifyInvoices;

public class ClassifyInvoicesRequest : IRequest<ClassifyInvoicesResponse>
{
    public List<string>? InvoiceIds { get; set; }
    public bool ManualTrigger { get; set; } = false;
}