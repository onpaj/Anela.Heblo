using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System.Text;
using Anela.Heblo.Adapters.Shoptet.Price;
using Anela.Heblo.Domain.Features.Catalog.Price;
using Anela.Heblo.Domain.Features.Catalog.Stock;
using Anela.Heblo.Domain.Features.Logistics.StockTaking;
using Anela.Heblo.Persistence.Logistics.StockTaking;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Anela.Heblo.Adapters.Shoptet;

public static class ShoptetAdapterServiceCollectionExtensions
{
    public static IServiceCollection AddShoptetCsvAdapter(this IServiceCollection services, IConfiguration configuration)
    {
        // Register code pages to support windows-1250 encoding used by Shoptet CSV exports
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

        services.AddHttpClient();

        services.AddSingleton<IProductPriceEshopClient, ShoptetPriceClient>();

        services.Configure<ProductPriceOptions>(configuration.GetSection(ProductPriceOptions.ConfigKey));

        services.TryAddSingleton(TimeProvider.System);

        services.AddScoped<IStockTakingRepository, StockTakingRepository>();

        return services;
    }
}
