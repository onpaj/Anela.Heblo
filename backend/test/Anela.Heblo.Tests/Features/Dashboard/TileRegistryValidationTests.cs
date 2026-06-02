using Anela.Heblo.Xcc.Services.Dashboard;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Anela.Heblo.Tests.Features.Dashboard;

public class TileRegistryValidationTests
{
    // Helper to build a minimal ServiceProvider with given tile types registered
    private static IServiceProvider BuildProvider(params Type[] tileTypes)
    {
        var services = new ServiceCollection();
        foreach (var tileType in tileTypes)
            services.AddScoped(tileType);
        return services.BuildServiceProvider();
    }

    [TileId("alpha")]
    private class AlphaTile : ITile
    {
        public string Title => "Alpha";
        public string Description => "Alpha";
        public TileSize Size => TileSize.Small;
        public TileCategory Category => TileCategory.System;
        public bool DefaultEnabled => false;
        public bool AutoShow => false;
        public Type ComponentType => typeof(object);
        public string[] RequiredPermissions => Array.Empty<string>();
        public Task<object> LoadDataAsync(Dictionary<string, string>? parameters = null, CancellationToken cancellationToken = default)
            => Task.FromResult<object>("alpha");
    }

    [TileId("alpha")]  // duplicate!
    private class AlphaDuplicateTile : ITile
    {
        public string Title => "Alpha Dup";
        public string Description => "Alpha Dup";
        public TileSize Size => TileSize.Small;
        public TileCategory Category => TileCategory.System;
        public bool DefaultEnabled => false;
        public bool AutoShow => false;
        public Type ComponentType => typeof(object);
        public string[] RequiredPermissions => Array.Empty<string>();
        public Task<object> LoadDataAsync(Dictionary<string, string>? parameters = null, CancellationToken cancellationToken = default)
            => Task.FromResult<object>("dup");
    }

    private class NoAttributeTile : ITile
    {
        public string Title => "No Attr";
        public string Description => "No Attr";
        public TileSize Size => TileSize.Small;
        public TileCategory Category => TileCategory.System;
        public bool DefaultEnabled => false;
        public bool AutoShow => false;
        public Type ComponentType => typeof(object);
        public string[] RequiredPermissions => Array.Empty<string>();
        public Task<object> LoadDataAsync(Dictionary<string, string>? parameters = null, CancellationToken cancellationToken = default)
            => Task.FromResult<object>("none");
    }

    [Fact]
    public void ValidateTileTypes_DuplicateIds_Throws()
    {
        var provider = BuildProvider(typeof(AlphaTile), typeof(AlphaDuplicateTile));
        var registry = new TileRegistry(provider);

        var act = () =>
        {
            registry.RegisterTile<AlphaTile>();
            registry.RegisterTile<AlphaDuplicateTile>();
            TileRegistryExtensions.ValidateTileTypes(new[] { typeof(AlphaTile), typeof(AlphaDuplicateTile) });
        };

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*alpha*");
    }

    [Fact]
    public void ValidateTileTypes_MissingAttribute_Throws()
    {
        var act = () =>
            TileRegistryExtensions.ValidateTileTypes(new[] { typeof(NoAttributeTile) });

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*NoAttributeTile*");
    }

    [Fact]
    public void ValidateTileTypes_ValidTiles_DoesNotThrow()
    {
        var act = () =>
            TileRegistryExtensions.ValidateTileTypes(new[] { typeof(AlphaTile) });

        act.Should().NotThrow();
    }
}
