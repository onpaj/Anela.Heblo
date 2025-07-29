namespace Anela.Heblo.Application.Domain.Catalog;

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
    public TimeSpan LotsRefreshInterval { get; set; } = TimeSpan.FromMinutes(60);


    public int SalesHistoryDays { get; set; } = 365;
    public int PurchaseHistoryDays { get; set; } = 365;
    public int ConsumedHistoryDays { get; set; } = 720;
}