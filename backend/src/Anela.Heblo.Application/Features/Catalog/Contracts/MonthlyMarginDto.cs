namespace Anela.Heblo.Application.Features.Catalog.Contracts;

public class MonthlyMarginDto
{
    public DateTime Month { get; set; }
    public MarginLevelDto M0 { get; set; } = new();
    public MarginLevelDto M1 { get; set; } = new();
    public MarginLevelDto M2 { get; set; } = new();
    public MarginLevelDto M3 { get; set; } = new();
}