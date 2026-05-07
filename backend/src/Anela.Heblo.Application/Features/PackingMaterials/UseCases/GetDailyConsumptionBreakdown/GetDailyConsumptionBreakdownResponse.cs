using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.PackingMaterials.UseCases.GetDailyConsumptionBreakdown;

public class GetDailyConsumptionBreakdownResponse : BaseResponse
{
    public string? Error { get; set; }
    public DateOnly Date { get; set; }
    public string GroupBy { get; set; } = string.Empty;
    public List<ConsumptionGroupDto> Groups { get; set; } = new();
}

public class ConsumptionGroupDto
{
    public string Key { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public decimal TotalAmount { get; set; }
    public int RowCount { get; set; }
    public List<ConsumptionDetailDto> Details { get; set; } = new();
}

public class ConsumptionDetailDto
{
    public string Key { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public decimal Amount { get; set; }
}
