namespace Anela.Heblo.Xcc.Services.Dashboard;

public interface ITile
{
    // Metadata properties - defined by the tile itself
    string Title { get; }
    string Description { get; }
    TileSize Size { get; }
    TileCategory Category { get; }
    bool DefaultEnabled { get; }
    bool AutoShow { get; }
    Type ComponentType { get; }
    string[] RequiredPermissions { get; }
    
    // Data loading
    Task<object> LoadDataAsync(CancellationToken cancellationToken = default);
}