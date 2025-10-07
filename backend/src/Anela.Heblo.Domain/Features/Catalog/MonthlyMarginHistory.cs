namespace Anela.Heblo.Domain.Features.Catalog;

public class MonthlyMarginHistory
{
    public List<MonthlyMarginData> MonthlyData { get; init; } = new();
    public MarginData Averages { get; init; } = new();
}