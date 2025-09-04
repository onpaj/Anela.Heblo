﻿using Anela.Heblo.Adapters.Flexi.Accounting.Ledger;
using Anela.Heblo.Adapters.Flexi.Lots;
using Anela.Heblo.Adapters.Flexi.Manufacture;
using Anela.Heblo.Adapters.Flexi.Materials;
using Anela.Heblo.Adapters.Flexi.Price;
using Anela.Heblo.Adapters.Flexi.ProductAttributes;
using Anela.Heblo.Adapters.Flexi.Purchase;
using Anela.Heblo.Adapters.Flexi.Sales;
using Anela.Heblo.Adapters.Flexi.Stock;
using Anela.Heblo.Application.Features.Purchase;
using Anela.Heblo.Domain.Accounting.Ledger;
using Anela.Heblo.Domain.Features.Catalog.Attributes;
using Anela.Heblo.Domain.Features.Catalog.ConsumedMaterials;
using Anela.Heblo.Domain.Features.Catalog.Price;
using Anela.Heblo.Domain.Features.Catalog.PurchaseHistory;
using Anela.Heblo.Domain.Features.Catalog.Sales;
using Anela.Heblo.Domain.Features.Catalog.Stock;
using Anela.Heblo.Domain.Features.Manufacture;
using Anela.Heblo.Xcc.Audit;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Rem.FlexiBeeSDK.Client.Clients.Contacts;
using Rem.FlexiBeeSDK.Client.Clients.Products.StockTaking;
using Rem.FlexiBeeSDK.Client.Clients.Products.StockToDate;
using Rem.FlexiBeeSDK.Client.DI;

namespace Anela.Heblo.Adapters.Flexi;

public static class FlexiAdapterServiceCollectionExtensions
{
    public static IServiceCollection AddFlexiAdapter(this IServiceCollection services, IConfiguration configuration)
    {

        services.AddFlexiBee(configuration);

        // Configure AutoMapper
        services.AddAutoMapper(typeof(FlexiAdapterServiceCollectionExtensions));

        // Add memory cache for FlexiProductPriceErpClient
        services.AddMemoryCache();

        services.AddHttpClient();

        // Add TimeProvider for FlexiStockClient
        services.AddSingleton(TimeProvider.System);

        services.AddSingleton<IErpStockDomainService, FlexiStockTakingDomainService>();

        services.AddSingleton<ICatalogAttributesClient, FlexiProductAttributesQueryClient>();
        services.AddSingleton<ICatalogSalesClient, FlexiCatalogSalesClient>();
        services.AddSingleton<IConsumedMaterialsClient, FlexiConsumedMaterialsQueryClient>();
        services.AddSingleton<IErpStockClient, FlexiStockClient>();
        services.AddSingleton<IProductPriceErpClient, FlexiProductPriceErpClient>();
        services.AddSingleton<IPurchaseHistoryClient, FlexiPurchaseHistoryQueryClient>();

        services.AddSingleton<IManufactureRepository, FlexiManufactureRepository>();
        services.AddSingleton<IManufactureHistoryClient, FlexiManufactureHistoryClient>();

        services.AddSingleton<ISupplierRepository, FlexiSupplierRepository>();

        services.AddSingleton<ILotsClient, LotsClient>();
        services.AddSingleton<Anela.Heblo.Domain.Features.Catalog.Lots.ILotsClient, FlexiLotsClient>();
        services.AddSingleton<ISeasonalDataParser, SeasonalDataParser>();
        services.AddSingleton<IStockTakingClient, StockTakingClient>();
        services.AddSingleton<IStockTakingItemsClient, StockTakingItemsClient>();
        services.AddSingleton<ILedgerService, LedgerService>();

        services.TryAddSingleton<IDataLoadAuditService, InMemoryDataLoadAuditService>();

        return services;
    }
}