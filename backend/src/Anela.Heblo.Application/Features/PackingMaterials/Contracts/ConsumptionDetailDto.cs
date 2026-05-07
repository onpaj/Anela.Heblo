namespace Anela.Heblo.Application.Features.PackingMaterials.Contracts;

public class ConsumptionDetailDto
{
    public string Key { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public decimal Amount { get; set; }
}
