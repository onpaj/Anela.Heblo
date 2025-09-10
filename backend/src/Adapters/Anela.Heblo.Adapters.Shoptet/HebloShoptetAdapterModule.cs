using Anela.Heblo.Adapters.Shoptet.Playwright;
using Anela.Heblo.Adapters.Shoptet.Playwright.Scenarios;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System.Text;
using Anela.Heblo.Adapters.Shoptet.IssuedInvoices;
using Anela.Heblo.Adapters.Shoptet.Price;
using Anela.Heblo.Adapters.Shoptet.Stock;
using Anela.Heblo.Domain.Features.CashRegister;
using Anela.Heblo.Domain.Features.Catalog.Price;
using Anela.Heblo.Domain.Features.Catalog.Stock;
using Anela.Heblo.Domain.Features.Invoices;
using Anela.Heblo.Domain.Features.Logistics.Picking;
using Anela.Heblo.Xcc.Audit;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Anela.Heblo.Adapters.Shoptet;

public static class ShoptetAdapterServiceCollectionExtensions
{
    public static IServiceCollection AddShoptetAdapter(this IServiceCollection services, IConfiguration configuration)
    {
        // Register code pages to support windows-1250 encoding used by Shoptet CSV exports
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

        // Configure AutoMapper
        services.AddAutoMapper(typeof(ShoptetAdapterServiceCollectionExtensions));

        services.AddHttpClient();
        services.AddSingleton<IIssuedInvoiceParser, XmlIssuedInvoiceParser>();
        services.AddSingleton<IIssuedInvoiceSource, ShoptetPlaywrightInvoiceSource>();

        services.AddSingleton<IssuedInvoiceExportScenario>();

        services.AddSingleton<IPickingListSource, ShoptetPlaywrightExpeditionListSource>();
        services.AddSingleton<PrintPickingListScenario>();

        services.AddSingleton<IEshopStockDomainService, ShoptetPlaywrightStockDomainService>();
        services.AddSingleton<StockUpScenario>();
        services.AddSingleton<StockTakingScenario>();

        services.AddSingleton<ICashRegisterOrdersSource, ShoptetPlaywrightCashRegisterOrdersSource>();
        services.AddSingleton<CashRegisterStatisticsScenario>();


        //services.Configure<DropBoxSourceOptions>(configuration.GetSection("Shoptet"));
        services.Configure<PlaywrightSourceOptions>(configuration.GetSection(PlaywrightSourceOptions.SettingsKey));
        services.AddSingleton(configuration.GetSection(PlaywrightSourceOptions.SettingsKey).Get<PlaywrightSourceOptions>());


        services.AddSingleton<IEshopStockClient, ShoptetStockClient>();
        services.Configure<ShoptetStockClientOptions>(
            configuration.GetSection(ShoptetStockClientOptions.SettingsKey));

        services.AddSingleton<IProductPriceEshopClient, ShoptetPriceClient>();

        services.Configure<ProductPriceOptions>(configuration.GetSection(ProductPriceOptions.ConfigKey));

        services.TryAddSingleton<IDataLoadAuditService, InMemoryDataLoadAuditService>();

        services.TryAddSingleton(TimeProvider.System);

        // TODO: Replace with real implementations when features are ready
        services.AddTransient<IStockTakingRepository, EmptyStockTakingRepository>();

        return services;
    }
}
