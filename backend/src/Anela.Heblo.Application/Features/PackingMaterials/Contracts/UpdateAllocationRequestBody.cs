namespace Anela.Heblo.Application.Features.PackingMaterials.Contracts;

public class UpdateAllocationRequestBody
{
    public string ProductCode { get; set; } = string.Empty;
    public decimal AmountPerUnit { get; set; }
}
