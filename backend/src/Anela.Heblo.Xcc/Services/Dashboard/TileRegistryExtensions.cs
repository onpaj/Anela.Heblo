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
        var types = RegisteredTileTypes.Distinct().ToList();

        ValidateTileTypes(types);

        foreach (var tileType in types)
        {
            var genericMethod = RegisterTileMethod.MakeGenericMethod(tileType);
            genericMethod.Invoke(registry, null);
        }
    }

    public static void ValidateTileTypes(IReadOnlyList<Type> tileTypes)
    {
        var errors = new List<string>();
        var seen = new Dictionary<string, Type>(StringComparer.Ordinal);

        foreach (var tileType in tileTypes)
        {
            string tileId;
            try
            {
                tileId = tileType.GetTileId();
            }
            catch (InvalidOperationException ex)
            {
                errors.Add(ex.Message);
                continue;
            }

            if (seen.TryGetValue(tileId, out var existing))
            {
                errors.Add(
                    $"Duplicate tile ID '{tileId}': " +
                    $"'{existing.FullName}' and '{tileType.FullName}' share the same ID.");
            }
            else
            {
                seen[tileId] = tileType;
            }
        }

        if (errors.Count > 0)
            throw new InvalidOperationException(
                "Tile registry validation failed:\n" + string.Join("\n", errors));
    }
}