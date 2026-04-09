using Microsoft.Extensions.DependencyInjection;

namespace Anela.Heblo.Application.Features.GridLayouts;

public static class GridLayoutsModule
{
    public static IServiceCollection AddGridLayoutsModule(this IServiceCollection services)
    {
        // MediatR handlers are auto-registered via assembly scanning.
        return services;
    }
}
