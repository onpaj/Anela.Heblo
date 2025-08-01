using Anela.Heblo.Xcc.Audit;
using Microsoft.Extensions.DependencyInjection;

namespace Anela.Heblo.Xcc;

/// <summary>
/// Cross-cutting concerns module registration
/// </summary>
public static class XccModule
{
    public static IServiceCollection AddXccServices(this IServiceCollection services)
    {
        // Register audit services
        services.AddSingleton<IDataLoadAuditService, InMemoryDataLoadAuditService>();

        return services;
    }
}