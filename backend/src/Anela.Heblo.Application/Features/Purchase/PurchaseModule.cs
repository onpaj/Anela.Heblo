using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
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
        // Register repositories using factory pattern to avoid ServiceProvider antipattern
        services.AddScoped<IPurchaseOrderRepository>(provider =>
        {
            var environment = provider.GetRequiredService<IHostEnvironment>();

            if (environment.EnvironmentName == "Test")
            {
                // Use in-memory implementations for testing
                return new InMemoryPurchaseOrderRepository();
            }
            else
            {
                // Use database implementations for real environments
                var context = provider.GetRequiredService<ApplicationDbContext>();
                return new PurchaseOrderRepository(context);
            }
        });

        services.AddScoped<IPurchaseOrderNumberGenerator>(provider =>
        {
            var environment = provider.GetRequiredService<IHostEnvironment>();

            if (environment.EnvironmentName == "Test")
            {
                // Use in-memory implementation for testing
                return new InMemoryPurchaseOrderNumberGenerator();
            }
            else
            {
                // Use standard implementation for real environments
                return new PurchaseOrderNumberGenerator();
            }
        });

        // Register stock severity calculator
        services.AddScoped<IStockSeverityCalculator, StockSeverityCalculator>();

        // Register validators
        services.AddScoped<IValidator<Model.CreatePurchaseOrderRequest>, CreatePurchaseOrderRequestValidator>();
        services.AddScoped<IValidator<Model.UpdatePurchaseOrderRequest>, UpdatePurchaseOrderRequestValidator>();

        return services;
    }
}