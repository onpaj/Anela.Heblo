using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.Purchase.UseCases.UpdatePurchaseOrderStatus;

public class UpdatePurchaseOrderStatusResponse : BaseResponse
{
    public int Id { get; set; }
    public string OrderNumber { get; set; } = null!;
    public string Status { get; set; } = null!;
    public DateTime? UpdatedAt { get; set; }
    public string? UpdatedBy { get; set; }

    public UpdatePurchaseOrderStatusResponse() : base() { }
    public UpdatePurchaseOrderStatusResponse(ErrorCodes errorCode, Dictionary<string, string>? parameters = null) : base(errorCode, parameters) { }
}