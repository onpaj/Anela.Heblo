using Microsoft.Extensions.DependencyInjection;
using Anela.Heblo.Application.Features.Purchase.Infrastructure;
using Anela.Heblo.Domain.Features.Purchase;

namespace Anela.Heblo.Application.Features.Purchase;

public static class PurchaseModule
{
    public static IServiceCollection AddPurchaseModule(this IServiceCollection services)
    {
        services.AddScoped<IPurchaseOrderRepository, PurchaseOrderRepository>();
        services.AddScoped<IPurchaseOrderNumberGenerator, PurchaseOrderNumberGenerator>();

        return services;
    }
}