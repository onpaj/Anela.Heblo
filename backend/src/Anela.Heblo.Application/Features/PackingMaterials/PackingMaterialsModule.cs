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

        // MediatR handlers are automatically registered by MediatR scan

        return services;
    }
}