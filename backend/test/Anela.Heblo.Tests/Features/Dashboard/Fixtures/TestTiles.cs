using Anela.Heblo.Xcc.Services.Dashboard;

namespace Anela.Heblo.Tests.Features.Dashboard.Fixtures;

[TileId("newautoshow")]
public class NewAutoShowTile : ITile
{
    public string Title { get; init; } = "Auto Show Tile";
    public string Description { get; init; } = "Auto show tile description";
    public TileSize Size { get; init; } = TileSize.Medium;
    public TileCategory Category { get; init; } = TileCategory.Finance;
    public bool DefaultEnabled { get; init; } = true;
    public bool AutoShow { get; init; } = true;
    public Type ComponentType { get; init; } = typeof(object);
    public string[] RequiredPermissions { get; init; } = Array.Empty<string>();

    public Task<object> LoadDataAsync(Dictionary<string, string>? parameters = null, CancellationToken cancellationToken = default)
    {
        return Task.FromResult((object)"Auto show data");
    }
}

[TileId("manual")]
public class ManualTile : ITile
{
    public string Title { get; init; } = "Manual Tile";
    public string Description { get; init; } = "Manual tile description";
    public TileSize Size { get; init; } = TileSize.Medium;
    public TileCategory Category { get; init; } = TileCategory.Finance;
    public bool DefaultEnabled { get; init; } = true;
    public bool AutoShow { get; init; } = false;
    public Type ComponentType { get; init; } = typeof(object);
    public string[] RequiredPermissions { get; init; } = Array.Empty<string>();

    public Task<object> LoadDataAsync(Dictionary<string, string>? parameters = null, CancellationToken cancellationToken = default)
    {
        return Task.FromResult((object)"Manual data");
    }
}

[TileId("auto1")]
public class AutoTile1 : ITile
{
    public string Title { get; init; } = "Auto Tile 1";
    public string Description { get; init; } = "Auto tile 1 description";
    public TileSize Size { get; init; } = TileSize.Medium;
    public TileCategory Category { get; init; } = TileCategory.Finance;
    public bool DefaultEnabled { get; init; } = true;
    public bool AutoShow { get; init; } = true;
    public Type ComponentType { get; init; } = typeof(object);
    public string[] RequiredPermissions { get; init; } = Array.Empty<string>();

    public Task<object> LoadDataAsync(Dictionary<string, string>? parameters = null, CancellationToken cancellationToken = default)
    {
        return Task.FromResult((object)"Auto tile 1 data");
    }
}

[TileId("auto2")]
public class AutoTile2 : ITile
{
    public string Title { get; init; } = "Auto Tile 2";
    public string Description { get; init; } = "Auto tile 2 description";
    public TileSize Size { get; init; } = TileSize.Medium;
    public TileCategory Category { get; init; } = TileCategory.Finance;
    public bool DefaultEnabled { get; init; } = true;
    public bool AutoShow { get; init; } = true;
    public Type ComponentType { get; init; } = typeof(object);
    public string[] RequiredPermissions { get; init; } = Array.Empty<string>();

    public Task<object> LoadDataAsync(Dictionary<string, string>? parameters = null, CancellationToken cancellationToken = default)
    {
        return Task.FromResult((object)"Auto tile 2 data");
    }
}

// Class-level [TileId] attribute for tile registration; separate from the constructor-injected TileId property
[TileId("testwithdata")]
public class TestTileWithData : ITile
{
    public string TileId { get; }
    public string Title { get; init; } = "Test Tile";
    public string Description { get; init; } = "Test Description";
    public TileSize Size { get; init; } = TileSize.Medium;
    public TileCategory Category { get; init; } = TileCategory.Finance;
    public bool DefaultEnabled { get; init; } = true;
    public bool AutoShow { get; init; } = false;
    public Type ComponentType { get; init; } = typeof(object);
    public string[] RequiredPermissions { get; init; } = Array.Empty<string>();

    public TestTileWithData(string tileId)
    {
        TileId = tileId;
    }

    public Task<object> LoadDataAsync(Dictionary<string, string>? parameters = null, CancellationToken cancellationToken = default)
    {
        return Task.FromResult((object)$"Test data for {TileId}");
    }
}
