namespace Anela.Heblo.Domain.Features.Manufacture.Inventory;

public class ManufacturedInventoryFilter
{
    public string? Search { get; init; }
    public bool OnlyWithStock { get; init; } = false;
    public int? ManufactureOrderId { get; init; }
    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 50;
}
