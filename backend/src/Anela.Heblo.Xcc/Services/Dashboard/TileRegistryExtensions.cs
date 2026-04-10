using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Anela.Heblo.Xcc.Services.Dashboard;

public static class TileRegistryExtensions
{
    private static readonly ConcurrentBag<Type> RegisteredTileTypes = new();

    private static readonly System.Reflection.MethodInfo RegisterTileMethod =
        typeof(ITileRegistry)
            .GetMethods()
            .Single(m => m.Name == nameof(ITileRegistry.RegisterTile) && m.IsGenericMethodDefinition);

    public static IServiceCollection RegisterTile<TTile>(
        this IServiceCollection services)
        where TTile : class, ITile
    {
        // Register tile in DI container
        services.AddScoped<TTile>();

        // Track tile type for later registration with registry
        RegisteredTileTypes.Add(typeof(TTile));

        return services;
    }

    public static void InitializeTileRegistry(this IHost app)
    {
        var registry = app.Services.GetRequiredService<ITileRegistry>();

        foreach (var tileType in RegisteredTileTypes.Distinct())
        {
            var genericMethod = RegisterTileMethod.MakeGenericMethod(tileType);
            genericMethod.Invoke(registry, null);
        }
    }
}