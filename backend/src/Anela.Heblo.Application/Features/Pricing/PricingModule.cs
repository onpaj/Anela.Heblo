using Anela.Heblo.Application.Common.Behaviors;
using Anela.Heblo.Application.Features.Pricing.Services;
using Anela.Heblo.Application.Features.Pricing.UseCases.RecalculatePricing;
using Anela.Heblo.Application.Features.Pricing.Validators;
using FluentValidation;
using MediatR;
using Microsoft.Extensions.DependencyInjection;

namespace Anela.Heblo.Application.Features.Pricing;

public static class PricingModule
{
    public static IServiceCollection AddPricingModule(this IServiceCollection services)
    {
        services.AddScoped<IPricingSimulationCalculator, PricingSimulationCalculator>();
        services.AddScoped<IPricingBaselineBuilder, PricingBaselineBuilder>();

        services.AddScoped<IValidator<RecalculatePricingRequest>, RecalculatePricingRequestValidator>();
        services.AddScoped<IPipelineBehavior<RecalculatePricingRequest, RecalculatePricingResponse>,
            ValidationBehavior<RecalculatePricingRequest, RecalculatePricingResponse>>();

        // MediatR handlers are auto-registered by the assembly scan in ApplicationModule.
        return services;
    }
}
