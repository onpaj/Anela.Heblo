### task: add-characterization-tests

**Files:**
- Modify: `backend/test/Anela.Heblo.Tests/Features/Catalog/GetProductMarginsHandlerTests.cs`

- [ ] **Step 1: Write two characterization tests for margin-field sorting**

Add the following two `[Fact]` methods to `GetProductMarginsHandlerTests`, placed after the existing `Handle_UnknownSortField_FallsBackToProductCodeAscending` test (around line 210, right before the closing brace of the class and the `BuildAggregate` helper):

```csharp
[Fact]
public async Task Handle_SortByM0AmountDescending_OrdersByCalculatedM0Amount()
{
    // Arrange
    _timeProviderMock
        .Setup(tp => tp.GetUtcNow())
        .Returns(new DateTimeOffset(2026, 6, 29, 12, 0, 0, TimeSpan.Zero));

    var catalogItems = new[]
    {
        BuildAggregateWithM0Margin(productCode: "LOW001", m0Amount: 10m, m0Percentage: 5m),
        BuildAggregateWithM0Margin(productCode: "HIGH001", m0Amount: 90m, m0Percentage: 45m),
        BuildAggregateWithM0Margin(productCode: "MID001", m0Amount: 50m, m0Percentage: 25m),
    };

    _catalogRepositoryMock
        .Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
        .ReturnsAsync(catalogItems);

    var request = new GetProductMarginsRequest
    {
        ProductType = ProductType.Product,
        SortBy = "m0amount",
        SortDescending = true,
        PageNumber = 1,
        PageSize = 100
    };

    // Act
    var response = await _handler.Handle(request, CancellationToken.None);

    // Assert
    response.Success.Should().BeTrue();
    response.Items.Select(i => i.ProductCode)
        .Should().ContainInOrder("HIGH001", "MID001", "LOW001");
}

[Fact]
public async Task Handle_SortByM0PercentageAscending_OrdersByCalculatedM0Percentage()
{
    // Arrange
    _timeProviderMock
        .Setup(tp => tp.GetUtcNow())
        .Returns(new DateTimeOffset(2026, 6, 29, 12, 0, 0, TimeSpan.Zero));

    var catalogItems = new[]
    {
        BuildAggregateWithM0Margin(productCode: "LOW001", m0Amount: 10m, m0Percentage: 5m),
        BuildAggregateWithM0Margin(productCode: "HIGH001", m0Amount: 90m, m0Percentage: 45m),
        BuildAggregateWithM0Margin(productCode: "MID001", m0Amount: 50m, m0Percentage: 25m),
    };

    _catalogRepositoryMock
        .Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
        .ReturnsAsync(catalogItems);

    var request = new GetProductMarginsRequest
    {
        ProductType = ProductType.Product,
        SortBy = "m0percentage",
        SortDescending = false,
        PageNumber = 1,
        PageSize = 100
    };

    // Act
    var response = await _handler.Handle(request, CancellationToken.None);

    // Assert
    response.Success.Should().BeTrue();
    response.Items.Select(i => i.ProductCode)
        .Should().ContainInOrder("LOW001", "MID001", "HIGH001");
}
```

Add the corresponding helper next to the existing `BuildAggregate` private static method (same class, just above or below it):

```csharp
private static CatalogAggregate BuildAggregateWithM0Margin(string productCode, decimal m0Amount, decimal m0Percentage)
{
    var aggregate = new CatalogAggregate
    {
        Id = productCode,
        ProductName = "Test Product",
        Type = ProductType.Product
    };

    aggregate.Margins.MonthlyData[new DateTime(2026, 1, 1)] = new MarginData
    {
        M0 = new MarginLevel(m0Percentage, m0Amount, 0m, 0m)
    };

    return aggregate;
}
```

`MonthlyMarginHistory.Averages` (accessed via `CatalogAggregate.Margins.Averages.M0`) averages across `MonthlyData` entries; with exactly one entry per aggregate, the average equals the single value supplied, so `m0Amount`/`m0Percentage` above map directly to `Margins.Averages.M0.Amount`/`.Percentage`.

- [ ] **Step 2: Run the new tests to confirm they already pass against the current (pre-refactor) code**

Run:
```bash
cd backend
dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~GetProductMarginsHandlerTests"
```
Expected: All tests in `GetProductMarginsHandlerTests` PASS, including the two new ones (`Handle_SortByM0AmountDescending_OrdersByCalculatedM0Amount`, `Handle_SortByM0PercentageAscending_OrdersByCalculatedM0Percentage`). This confirms the tests correctly characterize existing behavior before any refactor — they are not meant to fail here.

- [ ] **Step 3: Commit**

```bash
git add backend/test/Anela.Heblo.Tests/Features/Catalog/GetProductMarginsHandlerTests.cs
git commit -m "test(catalog): add characterization tests for margin-field sorting in GetProductMarginsHandler

Locks in current m0amount/m0percentage sort behavior before extracting
the SortBy helper in GetProductMarginsHandler.ApplySorting (#4287).

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01BTCy4YuKrWUcyza9b4ye1Z"
```

---

