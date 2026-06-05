using Anela.Heblo.Domain.Features.GridLayouts;
using Anela.Heblo.Persistence.GridLayouts;
using Microsoft.Extensions.DependencyInjection;

namespace Anela.Heblo.Application.Features.GridLayouts;

public static class GridLayoutsModule
{
    public static IServiceCollection AddGridLayoutsModule(this IServiceCollection services)
    {
        // Repository (implementation lives in the Persistence layer)
        services.AddScoped<IGridLayoutRepository, GridLayoutRepository>();

        // MediatR handlers are auto-registered via assembly scanning.
        return services;
    }
}
