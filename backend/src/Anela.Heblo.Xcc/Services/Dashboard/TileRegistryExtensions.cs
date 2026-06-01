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
        var distinctTypes = RegisteredTileTypes.Distinct().ToList();
        ValidateTileTypes(distinctTypes);

        var registry = app.Services.GetRequiredService<ITileRegistry>();
        foreach (var tileType in distinctTypes)
        {
            var genericMethod = RegisterTileMethod.MakeGenericMethod(tileType);
            genericMethod.Invoke(registry, null);
        }
    }

    internal static void ValidateTileTypes(IReadOnlyList<Type> types)
    {
        var duplicates = types
            .GroupBy(t => t.GetTileId())
            .Where(g => g.Count() > 1)
            .ToList();

        if (duplicates.Count == 0)
            return;

        var conflicts = duplicates.Select(g =>
            $"  ID '{g.Key}' is shared by: {string.Join(", ", g.Select(t => t.FullName))}");

        throw new InvalidOperationException(
            "Dashboard tile registry has duplicate tile IDs. Each tile must have a unique [TileId].\n" +
            string.Join("\n", conflicts));
    }
}