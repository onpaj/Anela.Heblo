// using Anela.Heblo.API.MCP.Tools;
using Microsoft.Extensions.DependencyInjection;

namespace Anela.Heblo.API.MCP;

/// <summary>
/// Dependency injection module for MCP server configuration.
/// Registers MCP tool classes and configures MCP server with tool discovery.
/// </summary>
public static class McpModule
{
    public static IServiceCollection AddMcpServices(this IServiceCollection services)
    {
        // Register MCP tool classes as transient (new instance per request)
        // Tools will be registered here as we create them
        // services.AddTransient<CatalogMcpTools>();
        // services.AddTransient<ManufactureOrderMcpTools>();
        // services.AddTransient<ManufactureBatchMcpTools>();

        // TODO: Register MCP server when Microsoft.Extensions.AI package is updated
        // services.AddMcpServer(options =>
        // {
        //     options.DiscoverToolsFrom<CatalogMcpTools>();
        //     options.DiscoverToolsFrom<ManufactureOrderMcpTools>();
        //     options.DiscoverToolsFrom<ManufactureBatchMcpTools>();
        // });

        return services;
    }
}
