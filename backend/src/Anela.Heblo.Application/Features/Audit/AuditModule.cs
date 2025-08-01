using Microsoft.Extensions.DependencyInjection;

namespace Anela.Heblo.Application.Features.Audit;

/// <summary>
/// Audit module for dependency injection registration
/// </summary>
public static class AuditModule
{
    /// <summary>
    /// Registers audit feature services
    /// </summary>
    public static IServiceCollection AddAuditModule(this IServiceCollection services)
    {
        // MediatR handlers are automatically registered by MediatR assembly scanning
        // No manual registration needed for handlers

        return services;
    }
}