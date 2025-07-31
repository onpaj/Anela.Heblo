using Microsoft.Extensions.DependencyInjection;

namespace Anela.Heblo.Application.Features.Configuration;

/// <summary>
/// Configuration module for dependency injection registration
/// </summary>
public static class ConfigurationModule
{
    /// <summary>
    /// Registers configuration feature services
    /// </summary>
    public static IServiceCollection AddConfigurationModule(this IServiceCollection services)
    {
        // MediatR handlers are automatically registered by MediatR assembly scanning
        // No manual registration needed for handlers

        return services;
    }
}