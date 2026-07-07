### task: add-test-fr1-default-product-type-filter

Add a test for FR-1: when `ProductType` is null, `ApplyFilters` returns only `Product` and
`Goods` items.

**Files:** `backend/test/Anela.Heblo.Tests/Features/Catalog/GetProductMarginsHandlerTests.cs`

1. Add the following test method inside `GetProductMarginsHandlerTests`, after the existing
   `Handle_UsesUtcNotLocalTime_AtDayBoundary` test and before the `BuildAggregate` helper:

   ```csharp
   [Fact]
   public async Task Handle_NullProductType_ReturnsOnlyProductAndGoods()
   {
       // Arrange
       _timeProviderMock
           .Setup(tp => tp.GetUtcNow())
           .Returns(new DateTimeOffset(2026, 6, 29, 12, 0, 0, TimeSpan.Zero));

       var catalogItems = new[]
       {
           BuildAggregate(productCode: "PROD001", type: ProductType.Product),
           BuildAggregate(productCode: "GOOD001", type: ProductType.Goods),
           BuildAggregate(productCode: "SEMI001", type: ProductType.SemiProduct),
           BuildAggregate(productCode: "MATI001", type: ProductType.Material),
       };

       _catalogRepositoryMock
           .Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
           .ReturnsAsync(catalogItems);

       var request = new GetProductMarginsRequest
       {
           ProductType = null,
           PageNumber = 1,
           PageSize = 100
       };

       // Act
       var response = await _handler.Handle(request, CancellationToken.None);

       // Assert
       response.Success.Should().BeTrue();
       response.TotalCount.Should().Be(2);
       response.Items.Should().HaveCount(2);
       response.Items.Select(i => i.ProductCode)
           .Should().BeEquivalentTo(new[] { "PROD001", "GOOD001" });
   }
   ```

2. Run only this test to confirm it passes:

   ```
   dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
       --filter "FullyQualifiedName~Handle_NullProductType_ReturnsOnlyProductAndGoods"
   ```

   Expected: `Passed! - 1`

3. Commit:
   ```
   git add backend/test/Anela.Heblo.Tests/Features/Catalog/GetProductMarginsHandlerTests.cs
   git commit -m "test: FR-1 default product-type filter returns only Product and Goods"
   ```
