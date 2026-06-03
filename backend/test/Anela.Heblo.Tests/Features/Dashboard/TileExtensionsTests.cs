using Anela.Heblo.Xcc.Services.Dashboard;
using FluentAssertions;
using Xunit;

namespace Anela.Heblo.Tests.Features.Dashboard;

public class TileExtensionsTests
{
    [TileId("mytile")]
    private class TileWithAttribute : ITile
    {
        public string Title => "Test";
        public string Description => "Test";
        public TileSize Size => TileSize.Small;
        public TileCategory Category => TileCategory.System;
        public bool DefaultEnabled => false;
        public bool AutoShow => false;
        public string[] RequiredPermissions => Array.Empty<string>();
        public Task<object> LoadDataAsync(Dictionary<string, string>? parameters = null, CancellationToken cancellationToken = default)
            => Task.FromResult<object>("data");
    }

    private class TileWithoutAttribute : ITile
    {
        public string Title => "No Attr";
        public string Description => "No Attr";
        public TileSize Size => TileSize.Small;
        public TileCategory Category => TileCategory.System;
        public bool DefaultEnabled => false;
        public bool AutoShow => false;
        public string[] RequiredPermissions => Array.Empty<string>();
        public Task<object> LoadDataAsync(Dictionary<string, string>? parameters = null, CancellationToken cancellationToken = default)
            => Task.FromResult<object>("data");
    }

    [Fact]
    public void GetTileId_Type_ReturnsAttributeValue()
    {
        var result = typeof(TileWithAttribute).GetTileId();
        result.Should().Be("mytile");
    }

    [Fact]
    public void GetTileId_Generic_ReturnsAttributeValue()
    {
        var result = TileExtensions.GetTileId<TileWithAttribute>();
        result.Should().Be("mytile");
    }

    [Fact]
    public void GetTileId_Instance_ReturnsAttributeValue()
    {
        ITile tile = new TileWithAttribute();
        var result = tile.GetTileId();
        result.Should().Be("mytile");
    }

    [Fact]
    public void GetTileId_MissingAttribute_ThrowsInvalidOperationException()
    {
        var act = () => typeof(TileWithoutAttribute).GetTileId();
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*TileWithoutAttribute*");
    }
}
