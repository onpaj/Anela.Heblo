using Anela.Heblo.Adapters.Flexi.Manufacture;
using Anela.Heblo.Adapters.Flexi.Materials;
using Anela.Heblo.Adapters.Flexi.Price;
using Anela.Heblo.Adapters.Flexi.ProductAttributes;
using Anela.Heblo.Adapters.Flexi.Purchase;
using Anela.Heblo.Adapters.Flexi.Sales;
using Anela.Heblo.Catalog.Attributes;
using Anela.Heblo.Catalog.Purchase;
using Anela.Heblo.Catalog.Sales;
using Anela.Heblo.Catalog.Stock;
using Anela.Heblo.Catalog.StockTaking;
using Anela.Heblo.ConsumedMaterials;
using Anela.Heblo.Manufacture;
using Anela.Heblo.Price;
using Microsoft.Extensions.DependencyInjection;
using Rem.FlexiBeeSDK.Client.Clients.Products.StockTaking;
using Rem.FlexiBeeSDK.Client.Clients.Products.StockToDate;
using Volo.Abp.AutoMapper;
using Volo.Abp.Modularity;

namespace Anela.Heblo.Adapters.Flexi;

[DependsOn(typeof(HebloDomainModule))]
public class HebloFlexiAdapterModule : AbpModule
{
    public override void ConfigureServices(ServiceConfigurationContext context)
    {
        var configuration = context.Services.GetConfiguration();
        
        context.Services.AddAutoMapperObjectMapper<HebloFlexiAdapterModule>();
        Configure<AbpAutoMapperOptions>(options =>
        {
            options.AddMaps<HebloFlexiAdapterModule>();
        });
        
        context.Services.AddSingleton<IErpStockTakingDomainService, FlexiStockTakingDomainService>();
        
        context.Services.AddSingleton<ICatalogAttributesClient, FlexiProductAttributesQueryClient>();
        context.Services.AddSingleton<ICatalogSalesClient, FlexiCatalogSalesClient>();
        context.Services.AddSingleton<IConsumedMaterialsClient, FlexiConsumedMaterialsQueryClient>();
        context.Services.AddSingleton<IErpStockClient, FlexiStockClient>();
        context.Services.AddSingleton<IProductPriceErpClient, FlexiProductPriceErpClient>();
        context.Services.AddSingleton<IPurchaseHistoryClient, FlexiPurchaseHistoryQueryClient>();
        context.Services.AddSingleton<IManufactureRepository, FlexiManufactureRepository>();
        context.Services.AddSingleton<ILotsClient, LotsClient>();
        context.Services.AddSingleton<ISeasonalDataParser, SeasonalDataParser>();
        
        context.Services.AddSingleton<IStockTakingClient, StockTakingClient>();
        context.Services.AddSingleton<IStockTakingItemsClient, StockTakingItemsClient>();
    }
}