using Microsoft.Extensions.DependencyInjection;

namespace Anela.Heblo.Application.Features.Manufacture;

public static class ManufactureModule
{
    public static IServiceCollection AddManufactureModule(this IServiceCollection services)
    {
        // Register MediatR handlers - they will be automatically discovered
        // No additional services needed for manufacturing stock analysis since it uses CatalogRepository directly

        return services;
    }
}