using System.Reflection;
using Anela.Heblo.Xcc.Services.Dashboard;
using Anela.Heblo.Xcc.Services.Dashboard.Tiles;
using FluentAssertions;
using Xunit;

namespace Anela.Heblo.Tests.Features.Dashboard;

public class TileIdContractTests
{
    // Scan only these two production assemblies (not the test assembly)
    private static readonly Assembly[] ProductionAssemblies =
    [
        typeof(BackgroundTaskStatusTile).Assembly,  // Anela.Heblo.Xcc
        typeof(Anela.Heblo.Application.Features.Purchase.DashboardTiles.LowStockEfficiencyTile).Assembly  // Anela.Heblo.Application
    ];

    private static IReadOnlyList<Type> GetConcreteTileTypes() =>
        ProductionAssemblies
            .SelectMany(a => a.GetTypes())
            .Where(t => t.IsClass && !t.IsAbstract && typeof(ITile).IsAssignableFrom(t))
            .ToList();

    [Fact]
    public void AllConcreteTiles_HaveTileIdAttribute()
    {
        var tileTypes = GetConcreteTileTypes();
        tileTypes.Should().NotBeEmpty("there must be at least one concrete ITile in the production assemblies");

        var missing = tileTypes
            .Where(t => t.GetCustomAttribute<TileIdAttribute>(inherit: false) is null)
            .Select(t => t.FullName!)
            .ToList();

        missing.Should().BeEmpty(
            $"every concrete ITile must have [TileId(\"...\")], but these are missing it: {string.Join(", ", missing)}");
    }

    [Fact]
    public void AllConcreteTiles_HaveLowercaseTileId()
    {
        var tileTypes = GetConcreteTileTypes()
            .Where(t => t.GetCustomAttribute<TileIdAttribute>(inherit: false) is not null)
            .ToList();

        var violations = tileTypes
            .Select(t => new { Type = t, Attr = t.GetCustomAttribute<TileIdAttribute>(inherit: false)! })
            .Where(x => x.Attr.Value != x.Attr.Value.ToLowerInvariant())
            .Select(x => $"{x.Type.Name}: \"{x.Attr.Value}\"")
            .ToList();

        violations.Should().BeEmpty(
            $"tile IDs must be lowercase, but these violate it: {string.Join(", ", violations)}");
    }

    [Fact]
    public void AllConcreteTiles_HaveUniqueTileIds()
    {
        var tileTypes = GetConcreteTileTypes()
            .Where(t => t.GetCustomAttribute<TileIdAttribute>(inherit: false) is not null)
            .ToList();

        var duplicates = tileTypes
            .GroupBy(t => t.GetCustomAttribute<TileIdAttribute>(inherit: false)!.Value, StringComparer.Ordinal)
            .Where(g => g.Count() > 1)
            .Select(g => $"ID '{g.Key}': {string.Join(", ", g.Select(t => t.Name))}")
            .ToList();

        duplicates.Should().BeEmpty(
            $"tile IDs must be unique, but these share IDs: {string.Join("; ", duplicates)}");
    }

    [Fact]
    public void AllConcreteTiles_WhoseNameEndsInTile_HaveBackwardCompatibleTileId()
    {
        // Guard: for every tile whose class name still ends with "Tile",
        // [TileId].Value must equal Name.ToLowerInvariant().Replace("tile", "").
        // This ensures persisted UserDashboardTiles rows continue to resolve.
        // To intentionally break this (e.g., after a rename migration), remove the class from scope here.
        var tileTypes = GetConcreteTileTypes()
            .Where(t => t.Name.EndsWith("Tile", StringComparison.Ordinal)
                        && t.GetCustomAttribute<TileIdAttribute>(inherit: false) is not null)
            .ToList();

        var violations = tileTypes
            .Select(t => new
            {
                Type = t,
                Actual = t.GetCustomAttribute<TileIdAttribute>(inherit: false)!.Value,
                Expected = t.Name.ToLowerInvariant().Replace("tile", "")
            })
            .Where(x => x.Actual != x.Expected)
            .Select(x => $"{x.Type.Name}: attribute=\"{x.Actual}\" expected=\"{x.Expected}\"")
            .ToList();

        violations.Should().BeEmpty(
            $"tiles whose class name ends in 'Tile' must have [TileId] matching the legacy derivation " +
            $"(name.ToLower().Replace(\"tile\",\"\")). Violations: {string.Join(", ", violations)}");
    }
}
