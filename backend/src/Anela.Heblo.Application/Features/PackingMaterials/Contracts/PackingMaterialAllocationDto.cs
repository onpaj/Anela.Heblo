namespace Anela.Heblo.Application.Features.PackingMaterials.Contracts;

public class PackingMaterialAllocationDto
{
    public int Id { get; set; }
    public int PackingMaterialId { get; set; }
    public string ProductCode { get; set; } = null!;
    public decimal AmountPerUnit { get; set; }
    public DateTime CreatedAt { get; set; }
}
