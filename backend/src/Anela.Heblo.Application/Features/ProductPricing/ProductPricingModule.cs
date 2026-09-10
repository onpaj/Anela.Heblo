using Anela.Heblo.Application.Common.Behaviors;
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
        services.AddScoped<IPriceComparisonService, PriceComparisonService>();
        services.AddScoped<IProductPriceChangeLogRepository, ProductPriceChangeLogRepository>();

        services.AddScoped<IValidator<SetProductPriceRequest>, SetProductPriceRequestValidator>();
        services.AddScoped<
            IPipelineBehavior<SetProductPriceRequest, SetProductPriceResponse>,
            ValidationBehavior<SetProductPriceRequest, SetProductPriceResponse>>();

        // MediatR handlers are automatically registered by assembly scan.

        return services;
    }
}
