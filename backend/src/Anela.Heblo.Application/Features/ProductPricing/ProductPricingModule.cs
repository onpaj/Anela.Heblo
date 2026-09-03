using Anela.Heblo.Application.Features.ProductPricing.Infrastructure.Jobs;
using Anela.Heblo.Application.Features.ProductPricing.Services;
using Anela.Heblo.Domain.Features.ProductPricing;
using Anela.Heblo.Persistence.ProductPricing;
using Microsoft.Extensions.DependencyInjection;

namespace Anela.Heblo.Application.Features.ProductPricing;

public static class ProductPricingModule
{
    public static IServiceCollection AddProductPricingModule(this IServiceCollection services)
    {
        services.AddScoped<IProductPriceRepository, ProductPriceRepository>();
        services.AddScoped<IProductPriceSyncService, ProductPriceSyncService>();

        services.AddScoped<ProductPriceSyncJob>();

        // Validator registrations are added by Tasks 8 and 9 as their use cases land.
        // There is no AddValidatorsFromAssembly in this project — each one is explicit.

        return services;
    }
}
