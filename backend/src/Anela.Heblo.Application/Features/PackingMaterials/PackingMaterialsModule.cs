using Anela.Heblo.Application.Features.PackingMaterials.Infrastructure.Jobs;
using Anela.Heblo.Application.Features.PackingMaterials.Services;
using Anela.Heblo.Domain.Features.PackingMaterials;
using Anela.Heblo.Persistence.PackingMaterials;
using Microsoft.Extensions.DependencyInjection;

namespace Anela.Heblo.Application.Features.PackingMaterials;

public static class PackingMaterialsModule
{
    public static IServiceCollection AddPackingMaterialsModule(this IServiceCollection services)
    {
        // Register repositories
        services.AddScoped<IPackingMaterialRepository, PackingMaterialRepository>();

        // Register services
        services.AddScoped<IConsumptionCalculationService, ConsumptionCalculationService>();

        // Register Hangfire jobs
        services.AddScoped<DailyConsumptionJob>();

        // MediatR handlers are automatically registered by MediatR scan

        return services;
    }
}