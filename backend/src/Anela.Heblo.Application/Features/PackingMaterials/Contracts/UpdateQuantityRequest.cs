namespace Anela.Heblo.Application.Features.PackingMaterials.Contracts;

public class UpdateQuantityRequest
{
    public decimal NewQuantity { get; set; }
    public DateOnly Date { get; set; }
}