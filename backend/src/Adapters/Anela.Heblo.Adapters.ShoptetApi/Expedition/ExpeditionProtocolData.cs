namespace Anela.Heblo.Adapters.ShoptetApi.Expedition;

public class ExpeditionProtocolData
{
    public string CarrierDisplayName { get; set; } = null!;
    public List<ExpeditionOrder> Orders { get; set; } = new();
}

public class ExpeditionOrder
{
    public string Code { get; set; } = null!;
    public string CustomerName { get; set; } = null!;
    public string Address { get; set; } = null!;
    public string Phone { get; set; } = null!;
    public string? CustomerRemark { get; set; }
    public string? EshopRemark { get; set; }
    public List<ExpeditionOrderItem> Items { get; set; } = new();
}

public class ExpeditionOrderItem
{
    public string ProductCode { get; set; } = null!;
    public string Name { get; set; } = null!;
    public string Variant { get; set; } = null!;
    public string WarehousePosition { get; set; } = null!;
    public int Quantity { get; set; }
    public decimal StockCount { get; set; }
    public decimal StockDemand { get; set; }
    public decimal UnitPrice { get; set; }
    public string Unit { get; set; } = string.Empty;
    public bool IsFromSet { get; set; }
    public string? SetName { get; set; }
}
