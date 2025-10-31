using MediatR;

namespace Anela.Heblo.Application.Features.InvoiceClassification.UseCases.ClassifyInvoices;

public class ClassifyInvoicesRequest : IRequest<ClassifyInvoicesResponse>
{
    public bool ManualTrigger { get; set; } = false;
}