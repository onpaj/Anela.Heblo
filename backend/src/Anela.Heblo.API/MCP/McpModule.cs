using Anela.Heblo.API.MCP.Tools;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.AspNetCore;

namespace Anela.Heblo.API.MCP;

/// <summary>
/// Dependency injection module for MCP server configuration.
/// Registers MCP server with official ModelContextProtocol.AspNetCore SDK and all tool classes.
/// </summary>
public static class McpModule
{
    public static IServiceCollection AddMcpServices(this IServiceCollection services)
    {
        services.AddMcpServer()
            .WithHttpTransport()
            .WithTools<CatalogMcpTools>()
            .WithTools<ManufactureOrderMcpTools>()
            .WithTools<ManufactureBatchMcpTools>();

        return services;
    }
}
