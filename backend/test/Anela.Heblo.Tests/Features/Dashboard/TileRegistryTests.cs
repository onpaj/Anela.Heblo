using Anela.Heblo.Xcc.Services.Dashboard;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Anela.Heblo.Tests.Features.Dashboard;

public class TileRegistryTests
{
    [Fact]
    public void GetTileMetadata_ShouldDisposeScope_BeforeReturning()
    {
        var tracker = new ScopeDisposalTracker();
        var services = new ServiceCollection();
        services.AddSingleton(tracker);
        services.AddScoped<TrackedTile>();
        var provider = services.BuildServiceProvider();

        var registry = new TileRegistry(provider);
        registry.RegisterTile<TrackedTile>();

        var metadata = registry.GetTileMetadata("tracked");

        metadata.Should().NotBeNull();
        tracker.WasDisposed.Should().BeTrue("the DI scope must be disposed before GetTileMetadata returns");
    }

    [Fact]
    public void GetAvailableTiles_ShouldDisposeScope_AfterEnumeratingAllTiles()
    {
        var tracker = new ScopeDisposalTracker();
        var services = new ServiceCollection();
        services.AddSingleton(tracker);
        services.AddScoped<TrackedTile>();
        var provider = services.BuildServiceProvider();

        var registry = new TileRegistry(provider);
        registry.RegisterTile<TrackedTile>();

        var tiles = registry.GetAvailableTiles().ToList();

        tiles.Should().HaveCount(1);
        tracker.WasDisposed.Should().BeTrue("the DI scope must be disposed after GetAvailableTiles returns");
    }

    [Fact]
    public void GetTileMetadata_ShouldReturnMetadataDecoupled_FromScopedDependencies()
    {
        var tracker = new ScopeDisposalTracker();
        var services = new ServiceCollection();
        services.AddSingleton(tracker);
        services.AddScoped<TrackedTile>();
        var provider = services.BuildServiceProvider();

        var registry = new TileRegistry(provider);
        registry.RegisterTile<TrackedTile>();

        var metadata = registry.GetTileMetadata("tracked");

        // Accessing metadata fields after scope disposal must not touch the tracker
        _ = metadata!.Title;
        _ = metadata.TileId;
        tracker.AccessCount.Should().Be(1, "tracker should only have been accessed once during construction, not when reading metadata");
    }
}

public class ScopeDisposalTracker
{
    public bool WasDisposed { get; private set; }
    public int AccessCount { get; private set; }

    public void RecordAccess() => AccessCount++;
    public void RecordDisposal() => WasDisposed = true;
}

[TileId("tracked")]
public class TrackedTile : ITile, IDisposable
{
    private readonly ScopeDisposalTracker _tracker;

    public TrackedTile(ScopeDisposalTracker tracker)
    {
        _tracker = tracker;
        _tracker.RecordAccess();
    }

    public string Title => "Tracked Tile";
    public string Description => "A tile that tracks scope disposal";
    public TileSize Size => TileSize.Small;
    public TileCategory Category => TileCategory.System;
    public bool DefaultEnabled => true;
    public bool AutoShow => false;
    public string[] RequiredPermissions => Array.Empty<string>();

    public Task<object> LoadDataAsync(Dictionary<string, string>? parameters = null, CancellationToken cancellationToken = default)
        => Task.FromResult<object>("tracked");

    public void Dispose() => _tracker.RecordDisposal();
}
