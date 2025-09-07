using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.Purchase.UseCases.UpdatePurchaseOrderInvoiceAcquired;

public class UpdatePurchaseOrderInvoiceAcquiredResponse : BaseResponse
{
    public int Id { get; set; }
    public bool InvoiceAcquired { get; set; }

    public UpdatePurchaseOrderInvoiceAcquiredResponse() : base() { }
    public UpdatePurchaseOrderInvoiceAcquiredResponse(ErrorCodes errorCode, Dictionary<string, string>? parameters = null) : base(errorCode, parameters) { }
}