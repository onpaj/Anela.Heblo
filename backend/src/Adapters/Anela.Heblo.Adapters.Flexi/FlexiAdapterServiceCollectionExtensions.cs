using Anela.Heblo.Adapters.Flexi.Manufacture;
using Anela.Heblo.Adapters.Flexi.Materials;
using Anela.Heblo.Adapters.Flexi.Price;
using Anela.Heblo.Adapters.Flexi.ProductAttributes;
using Anela.Heblo.Adapters.Flexi.Purchase;
using Anela.Heblo.Adapters.Flexi.Sales;
using Anela.Heblo.Application.Domain.Catalog.Attributes;
using Anela.Heblo.Application.Domain.Catalog.PurchaseHistory;
using Anela.Heblo.Application.Domain.Catalog.Sales;
using Anela.Heblo.Application.Domain.Catalog.Stock;
using Anela.Heblo.Application.Domain.Catalog.ConsumedMaterials;
using Anela.Heblo.Application.Domain.Catalog.Price;
using Anela.Heblo.Application.Domain.Logistics.Transport;
using Anela.Heblo.Application.Domain.Manufacture;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Rem.FlexiBeeSDK.Client.Clients.Products.StockTaking;
using Rem.FlexiBeeSDK.Client.Clients.Products.StockToDate;

namespace Anela.Heblo.Adapters.Flexi;

public static class FlexiAdapterServiceCollectionExtensions
{
    public static IServiceCollection AddFlexiAdapter(this IServiceCollection services, IConfiguration configuration)
    {
        // Configure AutoMapper
        services.AddAutoMapper(typeof(FlexiAdapterServiceCollectionExtensions));
        
        services.AddSingleton<IErpStockDomainService, FlexiStockTakingDomainService>();
        
        services.AddSingleton<ICatalogAttributesClient, FlexiProductAttributesQueryClient>();
        services.AddSingleton<ICatalogSalesClient, FlexiCatalogSalesClient>();
        services.AddSingleton<IConsumedMaterialsClient, FlexiConsumedMaterialsQueryClient>();
        services.AddSingleton<IErpStockClient, FlexiStockClient>();
        services.AddSingleton<IProductPriceErpClient, FlexiProductPriceErpClient>();
        services.AddSingleton<IPurchaseHistoryClient, FlexiPurchaseHistoryQueryClient>();
        services.AddSingleton<IManufactureRepository, FlexiManufactureRepository>();
        services.AddSingleton<ILotsClient, LotsClient>();
        services.AddSingleton<ISeasonalDataParser, SeasonalDataParser>();
        services.AddSingleton<IStockTakingClient, StockTakingClient>();
        services.AddSingleton<IStockTakingItemsClient, StockTakingItemsClient>();
        
        return services;
    }
}