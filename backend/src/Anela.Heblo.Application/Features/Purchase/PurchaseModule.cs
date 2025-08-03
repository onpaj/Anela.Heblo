using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Anela.Heblo.Application.Features.Purchase.Infrastructure;
using Anela.Heblo.Domain.Features.Purchase;

namespace Anela.Heblo.Application.Features.Purchase;

public static class PurchaseModule
{
    public static IServiceCollection AddPurchaseModule(this IServiceCollection services)
    {
        var serviceProvider = services.BuildServiceProvider();
        var environment = serviceProvider.GetService<IHostEnvironment>();

        if (environment?.EnvironmentName == "Automation" || environment?.EnvironmentName == "Test")
        {
            // Use in-memory implementations for testing
            services.AddSingleton<IPurchaseOrderRepository, InMemoryPurchaseOrderRepository>();
            services.AddScoped<IPurchaseOrderNumberGenerator, InMemoryPurchaseOrderNumberGenerator>();
        }
        else
        {
            // Use database implementations for real environments
            services.AddScoped<IPurchaseOrderRepository, PurchaseOrderRepository>();
            services.AddScoped<IPurchaseOrderNumberGenerator, PurchaseOrderNumberGenerator>();
        }

        return services;
    }
}