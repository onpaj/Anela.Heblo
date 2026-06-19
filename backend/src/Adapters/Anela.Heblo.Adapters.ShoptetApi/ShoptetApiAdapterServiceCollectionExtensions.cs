using System.Text;
using Anela.Heblo.Adapters.ShoptetApi.Customers;
using Anela.Heblo.Adapters.ShoptetApi.EshopUrl;
using Anela.Heblo.Adapters.ShoptetApi.Expedition;
using Anela.Heblo.Adapters.ShoptetApi.IssuedInvoices;
using Anela.Heblo.Adapters.ShoptetApi.IssuedInvoices.Mapping;
using Anela.Heblo.Adapters.ShoptetApi.Orders;
using Anela.Heblo.Adapters.ShoptetApi.Shipments;
using Anela.Heblo.Adapters.ShoptetApi.Stock;
using Anela.Heblo.Application.Features.ShipmentLabels;
using Anela.Heblo.Application.Features.ShoptetCustomers;
using Anela.Heblo.Application.Features.ShoptetOrders;
using Anela.Heblo.Domain.Features.Catalog.EshopUrl;
using Anela.Heblo.Domain.Features.Catalog.Stock;
using Anela.Heblo.Domain.Features.Logistics;
using Anela.Heblo.Application.Features.Logistics.Picking;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Polly;
using Polly.Retry;
using QuestPDF.Infrastructure;

namespace Anela.Heblo.Adapters.ShoptetApi;

public static class ShoptetApiAdapterServiceCollectionExtensions
{
    public static IServiceCollection AddShoptetApiAdapter(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

        QuestPDF.Settings.License = LicenseType.Community;

        services.AddOptions<ShoptetApiSettings>()
            .Bind(configuration.GetSection(ShoptetApiSettings.ConfigurationKey));

        services.AddHttpClient<ShoptetOrderClient>((sp, client) =>
        {
            var settings = sp.GetRequiredService<IOptions<ShoptetApiSettings>>().Value;
            client.BaseAddress = new Uri(settings.BaseUrl);
            client.DefaultRequestHeaders.Add("Shoptet-Private-API-Token", settings.ApiToken);
        });
        services.AddTransient<IEshopOrderClient>(sp => sp.GetRequiredService<ShoptetOrderClient>());
        services.AddTransient<IShoptetExpeditionOrderSource>(sp => sp.GetRequiredService<ShoptetOrderClient>());

        services.AddHttpClient<IEshopStockClient, ShoptetStockClient>((sp, client) =>
        {
            var settings = sp.GetRequiredService<IOptions<ShoptetApiSettings>>().Value;
            client.BaseAddress = new Uri(settings.BaseUrl);
            client.DefaultRequestHeaders.Add("Shoptet-Private-API-Token", settings.ApiToken);
            client.Timeout = Timeout.InfiniteTimeSpan;
        })
        .AddResilienceHandler("shoptet-stock-csv", (builder, context) =>
        {
            var opts = context.ServiceProvider.GetRequiredService<IOptions<ShoptetStockClientOptions>>().Value;
            var logger = context.ServiceProvider.GetRequiredService<ILoggerFactory>()
                .CreateLogger("ShoptetStockCsvResilience");

            builder
                .AddRetry(new HttpRetryStrategyOptions
                {
                    MaxRetryAttempts = opts.MaxRetryAttempts,
                    BackoffType = DelayBackoffType.Exponential,
                    UseJitter = true,
                    Delay = TimeSpan.FromSeconds(opts.RetryBaseDelaySeconds),
                    ShouldHandle = args =>
                    {
                        if (args.Outcome.Exception is OperationCanceledException oce &&
                            oce.CancellationToken.IsCancellationRequested)
                        {
                            return new ValueTask<bool>(false);
                        }
                        return new ValueTask<bool>(HttpClientResiliencePredicates.IsTransient(args.Outcome));
                    },
                    OnRetry = args =>
                    {
                        logger.LogWarning(
                            "Retrying {OperationName}. Attempt {AttemptNumber} of {MaxAttempts}. ExceptionType={ExceptionType} ExceptionMessage={ExceptionMessage}",
                            "ShoptetStockClient.ListAsync",
                            args.AttemptNumber + 1,
                            opts.MaxRetryAttempts,
                            args.Outcome.Exception?.GetType().Name,
                            args.Outcome.Exception?.Message);
                        return ValueTask.CompletedTask;
                    }
                })
                .AddTimeout(TimeSpan.FromSeconds(opts.TimeoutSeconds));
        });

        services.AddHttpClient<IShoptetInvoiceClient, ShoptetInvoiceClient>((sp, client) =>
        {
            var settings = sp.GetRequiredService<IOptions<ShoptetApiSettings>>().Value;
            client.BaseAddress = new Uri(settings.BaseUrl);
            client.DefaultRequestHeaders.Add("Shoptet-Private-API-Token", settings.ApiToken);
        });

        services.AddHttpClient<IShipmentClient, ShoptetShipmentClient>((sp, client) =>
        {
            var settings = sp.GetRequiredService<IOptions<ShoptetApiSettings>>().Value;
            client.BaseAddress = new Uri(settings.BaseUrl);
            client.DefaultRequestHeaders.Add("Shoptet-Private-API-Token", settings.ApiToken);
        });

        services.AddHttpClient<IShoptetCustomerClient, ShoptetCustomerClient>((sp, client) =>
        {
            var settings = sp.GetRequiredService<IOptions<ShoptetApiSettings>>().Value;
            client.BaseAddress = new Uri(settings.BaseUrl);
            client.DefaultRequestHeaders.Add("Shoptet-Private-API-Token", settings.ApiToken);
        });

        services.Configure<ShoptetStockClientOptions>(
            configuration.GetSection(ShoptetStockClientOptions.SettingsKey));

        services.AddTransient<IPickingListSource, ShoptetApiExpeditionListSource>();
        services.AddTransient<IPackingOrderClient, ShoptetApiPackingOrderClient>();

        services.AddHttpClient<IProductEshopUrlClient, HeurekaProductFeedClient>();
        services.Configure<HeurekaFeedOptions>(configuration.GetSection(HeurekaFeedOptions.ConfigKey));

        services.AddSingleton<BillingMethodMapper>();
        services.AddSingleton<ShippingMethodMapper>();
        services.AddSingleton<ShoptetInvoiceMapper>();
        services.AddSingleton<ShoptetApiInvoiceSource>();
        services.AddSingleton<IShippingMethodCatalog, ShippingMethodCatalog>();

        return services;
    }
}
