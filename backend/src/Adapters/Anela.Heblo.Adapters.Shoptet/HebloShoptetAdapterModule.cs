using Anela.Heblo.Adapters.Shoptet.Playwright;
using Anela.Heblo.Adapters.Shoptet.Playwright.Scenarios;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System.Text;
using Anela.Heblo.Adapters.Shoptet.IssuedInvoices;
using Anela.Heblo.Adapters.Shoptet.IssuedInvoices.ValueResolvers;
using Anela.Heblo.Adapters.Shoptet.EshopUrl;
using Anela.Heblo.Adapters.Shoptet.Price;
using Anela.Heblo.Domain.Features.CashRegister;
using Anela.Heblo.Domain.Features.Catalog.EshopUrl;
using Anela.Heblo.Domain.Features.Catalog.Price;
using Anela.Heblo.Domain.Features.Catalog.Stock;
using Anela.Heblo.Domain.Features.Invoices;
using Anela.Heblo.Domain.Features.Logistics.StockTaking;
using Anela.Heblo.Persistence.Logistics.StockTaking;
using Anela.Heblo.Persistence.Repositories;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Anela.Heblo.Adapters.Shoptet;

public static class ShoptetAdapterServiceCollectionExtensions
{
    public static IServiceCollection AddShoptetPlaywrightAdapter(this IServiceCollection services, IConfiguration configuration)
    {
        // Register code pages to support windows-1250 encoding used by Shoptet CSV exports
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

        // Configure AutoMapper
        services.AddAutoMapper(cfg => { }, typeof(ShoptetAdapterServiceCollectionExtensions));

        services.AddHttpClient();
        services.AddSingleton<IIssuedInvoiceParser, XmlIssuedInvoiceParser>();
        services.AddSingleton<IIssuedInvoiceSource, ShoptetPlaywrightInvoiceSource>();

        // Register invoice mapping resolvers
        services.AddSingleton<IPaymentMethodResolver, PaymentMethodResolver>();
        services.AddSingleton<IShippingMethodResolver, ShippingMethodResolver>();
        services.AddSingleton<IInvoicePriceCalculator, InvoicePriceCalculator>();

        // Register AutoMapper value resolvers as singletons (required by AutoMapper)
        services.AddSingleton<PaymentMethodValueResolver>();
        services.AddSingleton<ShippingMethodValueResolver>();
        services.AddSingleton<HomeCurrencyPriceValueResolver>();
        services.AddSingleton<ForeignCurrencyPriceValueResolver>();

        services.AddSingleton<IssuedInvoiceExportScenario>();

        services.AddSingleton<ICashRegisterOrdersSource, ShoptetPlaywrightCashRegisterOrdersSource>();
        services.AddSingleton<CashRegisterStatisticsScenario>();


        //services.Configure<DropBoxSourceOptions>(configuration.GetSection("Shoptet"));
        services.Configure<PlaywrightSourceOptions>(configuration.GetSection(PlaywrightSourceOptions.SettingsKey));
        services.AddSingleton(configuration.GetSection(PlaywrightSourceOptions.SettingsKey).Get<PlaywrightSourceOptions>());


        services.AddSingleton<IProductPriceEshopClient, ShoptetPriceClient>();

        services.Configure<ProductPriceOptions>(configuration.GetSection(ProductPriceOptions.ConfigKey));

        services.AddSingleton<IProductEshopUrlClient, HeurekaProductFeedClient>();
        services.Configure<HeurekaFeedOptions>(configuration.GetSection(HeurekaFeedOptions.ConfigKey));

        services.TryAddSingleton(TimeProvider.System);

        services.AddScoped<IStockTakingRepository, StockTakingRepository>();

        services.AddSingleton<PlaywrightBrowserFactory>();

        return services;
    }
}
