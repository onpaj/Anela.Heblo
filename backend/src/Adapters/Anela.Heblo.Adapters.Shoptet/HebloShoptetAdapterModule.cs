using Anela.Heblo.Adapters.Flexi.Price;
using Anela.Heblo.Adapters.Shoptet.Playwright;
using Anela.Heblo.Adapters.Shoptet.Playwright.Scenarios;
using Anela.Heblo.Catalog.Stock;
using Anela.Heblo.Catalog.StockTaking;
using Anela.Heblo.Invoices;
using Anela.Heblo.Logistics.Picking;
using Anela.Heblo.Price;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Volo.Abp.AutoMapper;
using Volo.Abp.Modularity;

namespace Anela.Heblo.Adapters.Shoptet;

[DependsOn(
    typeof(HebloDomainModule)
    )]
public class HebloShoptetAdapterModule : AbpModule
{
    public override void ConfigureServices(ServiceConfigurationContext context)
    {
        var configuration = context.Services.GetConfiguration();
        
        Configure<AbpAutoMapperOptions>(options =>
        {
            options.AddMaps<IssuedInvoiceMapping>();
        });

        context.Services.AddHttpClient();
        context.Services.AddSingleton<IIssuedInvoiceParser, XmlIssuedInvoiceParser>();
        context.Services.AddSingleton<IIssuedInvoiceSource, ShoptetPlaywrightInvoiceSource>();

        context.Services.AddSingleton<IssuedInvoiceExportScenario>();

        context.Services.AddSingleton<IPickingListSource, ShoptetPlaywrightExpeditionListSource>();
        context.Services.AddSingleton<PrintPickingListScenario>();
        
        context.Services.AddSingleton<IEshopStockTakingDomainService, ShoptetPlaywrightStockTakingDomainService>();
        context.Services.AddSingleton<StockUpScenario>();
        context.Services.AddSingleton<StockTakingScenario>();

        context.Services.AddSingleton<ICashRegisterOrdersSource, ShoptetPlaywrightCashRegisterOrdersSource>();
        context.Services.AddSingleton<CashRegisterStatisticsScenario>();


        //context.Services.Configure<DropBoxSourceOptions>(configuration.GetSection("Shoptet"));
        context.Services.Configure<PlaywrightSourceOptions>(configuration.GetSection(PlaywrightSourceOptions.SettingsKey));
        context.Services.AddSingleton(configuration.GetSection(PlaywrightSourceOptions.SettingsKey).Get<PlaywrightSourceOptions>());


        context.Services.AddSingleton<IEshopStockClient, ShoptetStockClient>();
        context.Services.Configure<ShoptetStockClientOptions>(
            configuration.GetSection(ShoptetStockClientOptions.SettingsKey));
        
        context.Services.AddSingleton<IProductPriceEshopClient, ShoptetPriceClient>();
                
        context.Services.Configure<ProductPriceOptions>(configuration.GetSection(ProductPriceOptions.ConfigKey));
    }
}
