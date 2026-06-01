using System.Reflection;
using Anela.Heblo.Application.Features.Dashboard.Tiles;
using Anela.Heblo.Xcc.Services.Dashboard;
using FluentAssertions;
using Xunit;

namespace Anela.Heblo.Tests.Features.Dashboard;

public class TileIdContractTests
{
    private static readonly IReadOnlyList<Type> ConcreteTileTypes = GetConcreteTileTypes();

    private static IReadOnlyList<Type> GetConcreteTileTypes()
    {
        var xccAssembly = typeof(ITile).Assembly;
        var appAssembly = typeof(PurchaseOrdersInTransitTile).Assembly;

        return new[] { xccAssembly, appAssembly }
            .SelectMany(a => a.GetTypes())
            .Where(t => t.IsClass && !t.IsAbstract && typeof(ITile).IsAssignableFrom(t))
            .ToList();
    }

    [Fact]
    public void AllConcreteTiles_ShouldHaveTileIdAttribute()
    {
        var missing = ConcreteTileTypes
            .Where(t => t.GetCustomAttribute<TileIdAttribute>(inherit: false) is null)
            .Select(t => t.FullName)
            .ToList();

        missing.Should().BeEmpty(
            because: $"every ITile must have [TileId(...)], but these are missing: {string.Join(", ", missing)}");
    }

    [Fact]
    public void AllConcreteTiles_ShouldHaveNonEmptyLowercaseTileId()
    {
        var violations = ConcreteTileTypes
            .Select(t => (Type: t, Attr: t.GetCustomAttribute<TileIdAttribute>(inherit: false)))
            .Where(x => x.Attr != null)
            .Where(x => string.IsNullOrWhiteSpace(x.Attr!.Value) || x.Attr.Value != x.Attr.Value.ToLowerInvariant())
            .Select(x => $"{x.Type.Name}: '{x.Attr!.Value}'")
            .ToList();

        violations.Should().BeEmpty(
            because: $"tile IDs must be non-empty and lowercase, violations: {string.Join(", ", violations)}");
    }

    [Fact]
    public void AllConcreteTiles_ShouldHaveUniqueTileIds()
    {
        var ids = ConcreteTileTypes
            .Select(t => t.GetCustomAttribute<TileIdAttribute>(inherit: false)?.Value)
            .Where(v => v != null)
            .ToList();

        var duplicates = ids
            .GroupBy(v => v)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();

        duplicates.Should().BeEmpty(
            because: $"tile IDs must be unique, duplicates: {string.Join(", ", duplicates)}");
    }

    [Fact]
    public void AllConcreteTiles_ShouldMatchLegacyDerivedValue_ForBackwardCompatibility()
    {
        // For every tile class whose name ends in "Tile", the [TileId] value must equal
        // className.ToLowerInvariant().Replace("tile", ""). This is the backward-compat guard
        // that ensures persisted UserDashboardTiles.TileId rows continue to resolve correctly.
        // To relax this for a specific tile, an EF Core migration updating the DB rows is required.
        var violations = ConcreteTileTypes
            .Where(t => t.Name.EndsWith("Tile", StringComparison.Ordinal))
            .Select(t => (Type: t, Attr: t.GetCustomAttribute<TileIdAttribute>(inherit: false)))
            .Where(x => x.Attr != null)
            .Select(x => (x.Type, x.Attr!.Value, Expected: x.Type.Name.ToLowerInvariant().Replace("tile", "")))
            .Where(x => x.Value != x.Expected)
            .Select(x => $"{x.Type.Name}: has '{x.Value}', expected '{x.Expected}'")
            .ToList();

        violations.Should().BeEmpty(
            because: $"tile IDs must match persisted DB values. Violations: {string.Join(", ", violations)}");
    }
}
