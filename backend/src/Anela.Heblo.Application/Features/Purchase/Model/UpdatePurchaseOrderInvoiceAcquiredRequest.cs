using MediatR;

namespace Anela.Heblo.Application.Features.Purchase.Model;

public class UpdatePurchaseOrderInvoiceAcquiredRequest : IRequest<UpdatePurchaseOrderInvoiceAcquiredResponse>
{
    public int Id { get; set; }
    public bool InvoiceAcquired { get; set; }
}