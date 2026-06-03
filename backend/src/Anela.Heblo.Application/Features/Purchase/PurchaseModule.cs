using Anela.Heblo.Application.Features.Catalog.Contracts;
using Anela.Heblo.Application.Features.Purchase.DashboardTiles;
using Anela.Heblo.Application.Features.Purchase.Infrastructure;
using Anela.Heblo.Application.Features.Purchase.Services;
using Anela.Heblo.Application.Features.Purchase.UseCases.CreatePurchaseOrder;
using Anela.Heblo.Application.Features.Purchase.UseCases.UpdatePurchaseOrder;
using Anela.Heblo.Domain.Features.Purchase;
using Anela.Heblo.Xcc.Services.Dashboard;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;

namespace Anela.Heblo.Application.Features.Purchase;

public static class PurchaseModule
{
    public static IServiceCollection AddPurchaseModule(this IServiceCollection services)
    {
        services.AddScoped<IPurchaseOrderNumberGenerator, PurchaseOrderNumberGenerator>();

        // Register stock severity calculator
        services.AddScoped<IStockSeverityCalculator, StockSeverityCalculator>();

        // Cross-module contract: Purchase implements Catalog's ICatalogPurchaseSource via adapter.
        // DI registration is owned by the provider (Purchase), not the consumer (Catalog).
        services.AddScoped<ICatalogPurchaseSource, PurchaseCatalogSourceAdapter>();

        // Register validators
        services.AddScoped<IValidator<CreatePurchaseOrderRequest>, CreatePurchaseOrderRequestValidator>();
        services.AddScoped<IValidator<UpdatePurchaseOrderRequest>, UpdatePurchaseOrderRequestValidator>();

        // Register dashboard tiles
        services.RegisterTile<LowStockEfficiencyTile>();
        services.RegisterTile<PurchaseOrdersInTransitTile>();

        return services;
    }
}