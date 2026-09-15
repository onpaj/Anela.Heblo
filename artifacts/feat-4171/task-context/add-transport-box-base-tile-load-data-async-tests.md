### task: add-transport-box-base-tile-load-data-async-tests

**Files:**
- Modify: `backend/test/Anela.Heblo.Tests/Features/Logistics/DashboardTiles/TransportBoxBaseTileTests.cs`

- [ ] **Step 1: Add the required `using` directives**

At the top of `backend/test/Anela.Heblo.Tests/Features/Logistics/DashboardTiles/TransportBoxBaseTileTests.cs`, the current using block is:

```csharp
using Anela.Heblo.Application.Features.Logistics.DashboardTiles;
using Anela.Heblo.Domain.Features.Logistics.Transport;
using FluentAssertions;
using Moq;
using System.Text.Json;
using Xunit;
```

Change it to (adding `System.Linq.Expressions`, needed for `Expression<Func<TransportBox, bool>>` in the repository mock setup):

```csharp
using Anela.Heblo.Application.Features.Logistics.DashboardTiles;
using Anela.Heblo.Domain.Features.Logistics.Transport;
using FluentAssertions;
using Moq;
using System.Linq.Expressions;
using System.Text.Json;
using Xunit;
```

- [ ] **Step 2: Write the failing test for the `LoadDataAsync` success path**

Immediately after the `GenerateDrillDownFilters_EmptyFilterStates_ReturnsEmptyObject` test method (before the `TestTransportBoxTile` nested class), add:

```csharp

    [Fact]
    public async Task LoadDataAsync_RepositorySucceeds_ReturnsSuccessStatusWithCount()
    {
        // Arrange
        var boxes = new List<TransportBox> { new(), new(), new() };
        _repositoryMock
            .Setup(x => x.FindAsync(
                It.IsAny<Expression<Func<TransportBox, bool>>>(),
                It.IsAny<bool>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(boxes);

        var tile = new TestTransportBoxTile(_repositoryMock.Object, new[] { TransportBoxState.Error });

        // Act
        var result = await tile.LoadDataAsync();

        // Assert
        var json = JsonSerializer.Serialize(result);
        using var doc = JsonDocument.Parse(json);

        doc.RootElement.GetProperty("status").GetString().Should().Be("success");
        doc.RootElement.GetProperty("data").GetProperty("count").GetInt32().Should().Be(3);
    }
```

- [ ] **Step 3: Write the failing test for the `LoadDataAsync` exception path**

Immediately after the test added in Step 2, add:

```csharp

    [Fact]
    public async Task LoadDataAsync_RepositoryThrows_ReturnsErrorShapeWithExceptionMessage()
    {
        // Arrange
        _repositoryMock
            .Setup(x => x.FindAsync(
                It.IsAny<Expression<Func<TransportBox, bool>>>(),
                It.IsAny<bool>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Database connection failed"));

        var tile = new TestTransportBoxTile(_repositoryMock.Object, new[] { TransportBoxState.Error });

        // Act
        var result = await tile.LoadDataAsync();

        // Assert
        var json = JsonSerializer.Serialize(result);
        using var doc = JsonDocument.Parse(json);

        doc.RootElement.GetProperty("status").GetString().Should().Be("error");
        doc.RootElement.GetProperty("error").GetString().Should().Be("Nepodařilo se načíst počet boxů");
        doc.RootElement.GetProperty("details").GetString().Should().Be("Database connection failed");
    }
```

- [ ] **Step 4: Run the new tests to verify they pass**

Run: `cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~TransportBoxBaseTileTests"`
Expected: 6 tests discovered (the 4 from the previous task plus `LoadDataAsync_RepositorySucceeds_ReturnsSuccessStatusWithCount` and `LoadDataAsync_RepositoryThrows_ReturnsErrorShapeWithExceptionMessage`), all passing.

- [ ] **Step 5: Commit**

```bash
git add backend/test/Anela.Heblo.Tests/Features/Logistics/DashboardTiles/TransportBoxBaseTileTests.cs
git commit -m "test(logistics): cover TransportBoxBaseTile.LoadDataAsync success and error paths"
```

---

