using Microsoft.Extensions.DependencyInjection;
using Anela.Heblo.Application.Features.Purchase.Infrastructure;
using Anela.Heblo.Domain.Features.Purchase;
using Anela.Heblo.Persistence;
using FluentValidation;
using Anela.Heblo.Application.Features.Purchase.Validators;

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
        services.AddScoped<IValidator<Model.CreatePurchaseOrderRequest>, CreatePurchaseOrderRequestValidator>();
        services.AddScoped<IValidator<Model.UpdatePurchaseOrderRequest>, UpdatePurchaseOrderRequestValidator>();

        return services;
    }
}