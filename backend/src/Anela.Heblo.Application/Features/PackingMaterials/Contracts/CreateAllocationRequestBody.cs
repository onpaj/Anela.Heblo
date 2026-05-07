namespace Anela.Heblo.Application.Features.PackingMaterials.Contracts;

public class CreateAllocationRequestBody
{
    public string ProductCode { get; set; } = string.Empty;
    public decimal AmountPerUnit { get; set; }
}
