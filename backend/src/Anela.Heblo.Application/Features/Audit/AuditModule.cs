using Anela.Heblo.Xcc.Audit;
using Microsoft.Extensions.DependencyInjection;

namespace Anela.Heblo.Application.Features.Audit;

/// <summary>
/// Audit module for in-memory audit logging
/// </summary>
public static class AuditModule
{
    /// <summary>
    /// Registers audit feature services with in-memory implementation only
    /// </summary>
    public static IServiceCollection AddAuditModule(this IServiceCollection services)
    {
        // Use in-memory audit service for all environments
        // Data is kept in memory and does not persist across application restarts
        services.AddSingleton<IDataLoadAuditService, InMemoryDataLoadAuditService>();

        // MediatR handlers are automatically registered by MediatR assembly scanning
        return services;
    }
}