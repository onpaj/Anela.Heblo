using Anela.Heblo.Application.Common.Behaviors;
using Anela.Heblo.Application.Features.Pricing.Services;
using Anela.Heblo.Application.Features.Pricing.UseCases.RecalculatePricing;
using Anela.Heblo.Application.Features.Pricing.Validators;
using Anela.Heblo.Domain.Features.Pricing;
using Anela.Heblo.Persistence.Pricing;
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

        // Repository (implementation lives in the Persistence layer)
        services.AddScoped<IPricingScenarioRepository, PricingScenarioRepository>();

        // MediatR handlers are auto-registered by the assembly scan in ApplicationModule.
        return services;
    }
}
