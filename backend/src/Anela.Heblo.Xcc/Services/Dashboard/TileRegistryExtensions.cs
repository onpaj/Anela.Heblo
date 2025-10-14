using Microsoft.Extensions.DependencyInjection;

namespace Anela.Heblo.Xcc.Services.Dashboard;

public static class TileRegistryExtensions
{
    private static readonly List<Type> RegisteredTileTypes = new();

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

    public static void InitializeTileRegistry(IServiceProvider serviceProvider)
    {
        var registry = serviceProvider.GetRequiredService<ITileRegistry>();

        foreach (var tileType in RegisteredTileTypes)
        {
            var method = typeof(ITileRegistry).GetMethod(nameof(ITileRegistry.RegisterTile));
            var genericMethod = method!.MakeGenericMethod(tileType);
            genericMethod.Invoke(registry, null);
        }
    }
}