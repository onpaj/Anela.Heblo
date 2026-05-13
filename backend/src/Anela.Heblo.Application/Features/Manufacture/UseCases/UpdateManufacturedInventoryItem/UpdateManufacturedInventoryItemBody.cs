namespace Anela.Heblo.Application.Features.Manufacture.UseCases.UpdateManufacturedInventoryItem;

public class UpdateManufacturedInventoryItemBody
{
    public decimal NewAmount { get; init; }
    public string? Note { get; init; }
}
