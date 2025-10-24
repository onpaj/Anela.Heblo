namespace Anela.Heblo.Application.Common;

public class DataSourceOptions
{
    public const string ConfigKey = "DataSourceOptions";
    public int SalesHistoryDays { get; set; } = 400;
    public int PurchaseHistoryDays { get; set; } = 400;
    public int ConsumedHistoryDays { get; set; } = 720;
    public int ManufactureHistoryDays { get; set; } = 400;

    public int ManufactureCostHistoryDays { get; set; } = 400;

    // Low stock alert tile configuration
    public double ResupplyThresholdMultiplier { get; set; } = 1.3;
}