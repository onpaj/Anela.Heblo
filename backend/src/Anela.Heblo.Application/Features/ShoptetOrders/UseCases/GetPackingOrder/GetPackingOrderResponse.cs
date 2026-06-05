using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Shared;

namespace Anela.Heblo.Application.Features.ShoptetOrders.UseCases.GetPackingOrder;

public class GetPackingOrderResponse : BaseResponse
{
    public GetPackingOrderResponse()
    {
    }

    public GetPackingOrderResponse(ErrorCodes errorCode, Dictionary<string, string>? @params = null)
        : base(errorCode, @params)
    {
    }

    public string Code { get; set; } = string.Empty;
    public string CustomerName { get; set; } = string.Empty;
    public string ShippingMethodName { get; set; } = string.Empty;
    public Cooling Cooling { get; set; } = Cooling.None;
    public bool IsCooled { get; set; }
    public PackingEligibility Eligibility { get; set; } = new();
    public string? CustomerNote { get; set; }
    public string? EshopNote { get; set; }
    public string? ShippingStreet { get; set; }
    public string? ShippingCity { get; set; }
    public string? ShippingZip { get; set; }
    public List<PackingOrderItem> Items { get; set; } = new();
}
