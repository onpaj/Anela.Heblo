using Anela.Heblo.Application.Common.Behaviors;
using Anela.Heblo.Application.Features.ProductPricing.Infrastructure.Jobs;
using Anela.Heblo.Application.Features.ProductPricing.Services;
using Anela.Heblo.Application.Features.ProductPricing.UseCases.SetProductPrice;
using Anela.Heblo.Domain.Features.ProductPricing;
using Anela.Heblo.Persistence.ProductPricing;
using FluentValidation;
using MediatR;
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
        services.AddScoped<IValidator<SetProductPriceRequest>, SetProductPriceRequestValidator>();
        services.AddScoped<
            IPipelineBehavior<SetProductPriceRequest, SetProductPriceResponse>,
            ValidationBehavior<SetProductPriceRequest, SetProductPriceResponse>>();

        // MediatR handlers are automatically registered by assembly scan.

        return services;
    }
}
