using Anela.Heblo.Application.Features.Pricing.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Anela.Heblo.Application.Features.Pricing;

public static class PricingModule
{
    public static IServiceCollection AddPricingModule(this IServiceCollection services)
    {
        services.AddScoped<IPricingSimulationCalculator, PricingSimulationCalculator>();
        services.AddScoped<IPricingBaselineBuilder, PricingBaselineBuilder>();

        // MediatR handlers are auto-registered by the assembly scan in ApplicationModule.
        return services;
    }
}
