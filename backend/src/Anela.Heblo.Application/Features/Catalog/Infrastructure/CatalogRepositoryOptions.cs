namespace Anela.Heblo.Application.Features.Catalog;

public class CatalogRepositoryOptions
{
    public const string ConfigKey = "CatalogRepositoryOptions";

    public TimeSpan ErpStockRefreshInterval { get; set; } = TimeSpan.FromMinutes(10);

    public TimeSpan EshopStockRefreshInterval { get; set; } = TimeSpan.FromMinutes(5);

    public TimeSpan AttributesRefreshInterval { get; set; } = TimeSpan.FromMinutes(60);

    public TimeSpan SalesRefreshInterval { get; set; } = TimeSpan.FromMinutes(60);

    public TimeSpan ConsumedRefreshInterval { get; set; } = TimeSpan.FromMinutes(60);

    public TimeSpan TransportRefreshInterval { get; set; } = TimeSpan.FromMinutes(5);
    public TimeSpan ReserveRefreshInterval { get; set; } = TimeSpan.FromMinutes(5);
    public TimeSpan StockTakingRefreshInterval { get; set; } = TimeSpan.FromMinutes(5);
    public TimeSpan PurchaseHistoryRefreshInterval { get; set; } = TimeSpan.FromMinutes(60);
    public TimeSpan ManufactureHistoryRefreshInterval { get; set; } = TimeSpan.FromMinutes(60);
    public TimeSpan LotsRefreshInterval { get; set; } = TimeSpan.FromMinutes(60);

    public TimeSpan EshopPricesRefreshInterval { get; set; } = TimeSpan.FromMinutes(30);
    public TimeSpan ErpPricesRefreshInterval { get; set; } = TimeSpan.FromMinutes(60);
    public TimeSpan ManufactureDifficultyRefreshInterval { get; set; } = TimeSpan.FromHours(1);

    public int SalesHistoryDays { get; set; } = 400;
    public int PurchaseHistoryDays { get; set; } = 400;
    public int ConsumedHistoryDays { get; set; } = 720;
    public int ManufactureHistoryDays { get; set; } = 400;
}