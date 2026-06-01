using Anela.Heblo.Xcc.Services.Dashboard;
using FluentAssertions;
using Xunit;

namespace Anela.Heblo.Tests.Features.Dashboard;

public class TileRegistryValidationTests
{
    [Fact]
    public void ValidateTileTypes_ShouldThrow_WhenTwoTilesShareTheSameTileId()
    {
        var types = new List<Type> { typeof(DuplicateTileA), typeof(DuplicateTileB) };

        var act = () => TileRegistryExtensions.ValidateTileTypes(types);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*duplicate*");
    }

    [Fact]
    public void ValidateTileTypes_ShouldThrow_WhenTileIsMissingTileIdAttribute()
    {
        var types = new List<Type> { typeof(NoAttributeTile) };

        var act = () => TileRegistryExtensions.ValidateTileTypes(types);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*missing*");
    }

    [TileId("duplicate")]
    private class DuplicateTileA : ITile
    {
        public string Title => "Duplicate A";
        public string Description => "Duplicate tile A";
        public TileSize Size => TileSize.Small;
        public TileCategory Category => TileCategory.System;
        public bool DefaultEnabled => true;
        public bool AutoShow => false;
        public Type ComponentType => typeof(object);
        public string[] RequiredPermissions => Array.Empty<string>();

        public Task<object> LoadDataAsync(Dictionary<string, string>? parameters = null, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();
    }

    [TileId("duplicate")]
    private class DuplicateTileB : ITile
    {
        public string Title => "Duplicate B";
        public string Description => "Duplicate tile B";
        public TileSize Size => TileSize.Small;
        public TileCategory Category => TileCategory.System;
        public bool DefaultEnabled => true;
        public bool AutoShow => false;
        public Type ComponentType => typeof(object);
        public string[] RequiredPermissions => Array.Empty<string>();

        public Task<object> LoadDataAsync(Dictionary<string, string>? parameters = null, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();
    }

    // No [TileId] attribute
    private class NoAttributeTile : ITile
    {
        public string Title => "No Attribute";
        public string Description => "Tile missing TileId attribute";
        public TileSize Size => TileSize.Small;
        public TileCategory Category => TileCategory.System;
        public bool DefaultEnabled => true;
        public bool AutoShow => false;
        public Type ComponentType => typeof(object);
        public string[] RequiredPermissions => Array.Empty<string>();

        public Task<object> LoadDataAsync(Dictionary<string, string>? parameters = null, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();
    }
}
