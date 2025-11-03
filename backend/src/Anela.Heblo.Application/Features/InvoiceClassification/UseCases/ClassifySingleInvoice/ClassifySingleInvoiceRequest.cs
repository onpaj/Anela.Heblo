using MediatR;

namespace Anela.Heblo.Application.Features.InvoiceClassification.UseCases.ClassifySingleInvoice;

public class ClassifySingleInvoiceRequest : IRequest<ClassifySingleInvoiceResponse>
{
    public string InvoiceId { get; set; } = string.Empty;
}