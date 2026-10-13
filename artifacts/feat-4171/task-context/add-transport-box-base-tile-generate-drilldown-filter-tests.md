### task: add-transport-box-base-tile-generate-drilldown-filter-tests

**Files:**
- Create: `backend/test/Anela.Heblo.Tests/Features/Logistics/DashboardTiles/TransportBoxBaseTileTests.cs`

- [ ] **Step 1: Write the failing tests for all four `GenerateDrillDownFilters` branches**

Create `backend/test/Anela.Heblo.Tests/Features/Logistics/DashboardTiles/TransportBoxBaseTileTests.cs` with this content:

```csharp
using Anela.Heblo.Application.Features.Logistics.DashboardTiles;
using Anela.Heblo.Domain.Features.Logistics.Transport;
using FluentAssertions;
using Moq;
using System.Text.Json;
using Xunit;

namespace Anela.Heblo.Tests.Features.Logistics.DashboardTiles;

public class TransportBoxBaseTileTests
{
    private readonly Mock<ITransportBoxRepository> _repositoryMock;

    public TransportBoxBaseTileTests()
    {
        _repositoryMock = new Mock<ITransportBoxRepository>();
    }

    [Fact]
    public void GenerateDrillDownFilters_SingleState_ReturnsThatStateAsFilter()
    {
        // Arrange
        var tile = new TestTransportBoxTile(_repositoryMock.Object, new[] { TransportBoxState.Error });

        // Act
        var result = tile.CallGenerateDrillDownFilters();

        // Assert
        var json = JsonSerializer.Serialize(result);
        using var doc = JsonDocument.Parse(json);

        doc.RootElement.GetProperty("state").GetString().Should().Be("Error");
    }

    [Fact]
    public void GenerateDrillDownFilters_MultipleStatesAllNonClosed_ReturnsActiveSentinel()
    {
        // Arrange
        var tile = new TestTransportBoxTile(
            _repositoryMock.Object,
            new[] { TransportBoxState.New, TransportBoxState.Opened, TransportBoxState.InTransit });

        // Act
        var result = tile.CallGenerateDrillDownFilters();

        // Assert
        var json = JsonSerializer.Serialize(result);
        using var doc = JsonDocument.Parse(json);

        doc.RootElement.GetProperty("state").GetString().Should().Be("ACTIVE");
    }

    [Fact]
    public void GenerateDrillDownFilters_MultipleStatesIncludingClosed_ReturnsFirstStateNotActiveSentinel()
    {
        // Arrange
        var tile = new TestTransportBoxTile(
            _repositoryMock.Object,
            new[] { TransportBoxState.InTransit, TransportBoxState.Closed });

        // Act
        var result = tile.CallGenerateDrillDownFilters();

        // Assert
        var json = JsonSerializer.Serialize(result);
        using var doc = JsonDocument.Parse(json);

        var state = doc.RootElement.GetProperty("state").GetString();
        state.Should().Be("InTransit");
        state.Should().NotBe("ACTIVE");
    }

    [Fact]
    public void GenerateDrillDownFilters_EmptyFilterStates_ReturnsEmptyObject()
    {
        // Arrange
        var tile = new TestTransportBoxTile(_repositoryMock.Object, Array.Empty<TransportBoxState>());

        // Act
        var result = tile.CallGenerateDrillDownFilters();

        // Assert
        var json = JsonSerializer.Serialize(result);
        using var doc = JsonDocument.Parse(json);

        doc.RootElement.EnumerateObject().Should().BeEmpty();
    }

    // Test-only concrete subclass: TransportBoxBaseTile is abstract and its FilterStates is
    // fixed per real tile (see ErrorBoxesTile, InTransitBoxesTile, ReceivedBoxesTile), so no
    // existing concrete tile can express every branch under test here. This type exists only to
    // make FilterStates configurable per test case and to expose the protected
    // GenerateDrillDownFilters() method for direct invocation. It is defined in the test
    // assembly only, is never registered with the dashboard tile registry, and is invisible to
    // TileIdContractTests (which scans only the Anela.Heblo.Xcc and Anela.Heblo.Application
    // production assemblies), so it needs no [TileId(...)] attribute.
    private sealed class TestTransportBoxTile : TransportBoxBaseTile
    {
        public override string Title => "Test Tile";
        public override string Description => "Test Tile Description";

        private readonly TransportBoxState[] _filterStates;
        protected override TransportBoxState[] FilterStates => _filterStates;

        public TestTransportBoxTile(ITransportBoxRepository repository, TransportBoxState[] filterStates)
            : base(repository)
        {
            _filterStates = filterStates;
        }

        public object CallGenerateDrillDownFilters() => GenerateDrillDownFilters();
    }
}
```

- [ ] **Step 2: Run the new tests to verify they pass**

Run: `cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~TransportBoxBaseTileTests"`
Expected: 4 tests discovered (`GenerateDrillDownFilters_SingleState_ReturnsThatStateAsFilter`, `GenerateDrillDownFilters_MultipleStatesAllNonClosed_ReturnsActiveSentinel`, `GenerateDrillDownFilters_MultipleStatesIncludingClosed_ReturnsFirstStateNotActiveSentinel`, `GenerateDrillDownFilters_EmptyFilterStates_ReturnsEmptyObject`), all passing. (These tests target existing, already-correct production logic, so they are expected to pass immediately — there is no red/green cycle against unwritten production code here, only against the new test file itself; if any fails, the test file has a bug, not `TransportBoxBaseTile.cs`.)

- [ ] **Step 3: Commit**

```bash
git add backend/test/Anela.Heblo.Tests/Features/Logistics/DashboardTiles/TransportBoxBaseTileTests.cs
git commit -m "test(logistics): cover TransportBoxBaseTile.GenerateDrillDownFilters branches"
```

---

