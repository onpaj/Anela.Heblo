using Microsoft.Extensions.DependencyInjection;
using Anela.Heblo.Application.Features.Purchase.Services;
using Anela.Heblo.Application.Features.Purchase.UseCases.CreatePurchaseOrder;
using Anela.Heblo.Application.Features.Purchase.UseCases.UpdatePurchaseOrder;
using Anela.Heblo.Domain.Features.Purchase;
using Anela.Heblo.Persistence;
using Anela.Heblo.Persistence.Purchase.PurchaseOrders;
using FluentValidation;

namespace Anela.Heblo.Application.Features.Purchase;

public static class PurchaseModule
{
    public static IServiceCollection AddPurchaseModule(this IServiceCollection services)
    {
        // Register default implementations - tests can override these
        services.AddScoped<IPurchaseOrderRepository>(provider =>
        {
            var context = provider.GetRequiredService<ApplicationDbContext>();
            return new PurchaseOrderRepository(context);
        });

        services.AddScoped<IPurchaseOrderNumberGenerator, PurchaseOrderNumberGenerator>();

        // Register stock severity calculator
        services.AddScoped<IStockSeverityCalculator, StockSeverityCalculator>();

        // Register validators
        services.AddScoped<IValidator<CreatePurchaseOrderRequest>, CreatePurchaseOrderRequestValidator>();
        services.AddScoped<IValidator<UpdatePurchaseOrderRequest>, UpdatePurchaseOrderRequestValidator>();

        return services;
    }
}