namespace Anela.Heblo.Application.Features.PackingMaterials.Contracts;

public class ConsumptionGroupDto
{
    public string Key { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public decimal TotalAmount { get; set; }
    public int RowCount { get; set; }
    public List<ConsumptionDetailDto> Details { get; set; } = new();
}
