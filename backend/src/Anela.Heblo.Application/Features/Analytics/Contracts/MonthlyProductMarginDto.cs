namespace Anela.Heblo.Application.Features.Analytics.Contracts;

public class MonthlyProductMarginDto
{
    public int Year { get; set; }
    public int Month { get; set; }
    public string MonthDisplay { get; set; } = string.Empty; // "Led 2024"
    public List<ProductMarginSegmentDto> ProductSegments { get; set; } = new();
    public decimal TotalMonthMargin { get; set; }
}