### task: add-test-fr2-explicit-product-type-filter

Add a test for FR-2: when `ProductType` has a value, only items with that exact type are
returned.

**Files:** `backend/test/Anela.Heblo.Tests/Features/Catalog/GetProductMarginsHandlerTests.cs`

1. Add the following test method after the FR-1 test, before `BuildAggregate`:

   ```csharp
   [Fact]
   public async Task Handle_ExplicitProductType_ReturnsOnlyMatchingType()
   {
       // Arrange
       _timeProviderMock
           .Setup(tp => tp.GetUtcNow())
           .Returns(new DateTimeOffset(2026, 6, 29, 12, 0, 0, TimeSpan.Zero));

       var catalogItems = new[]
       {
           BuildAggregate(productCode: "PROD001", type: ProductType.Product),
           BuildAggregate(productCode: "SEMI001", type: ProductType.SemiProduct),
       };

       _catalogRepositoryMock
           .Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
           .ReturnsAsync(catalogItems);

       var request = new GetProductMarginsRequest
       {
           ProductType = ProductType.SemiProduct,
           PageNumber = 1,
           PageSize = 100
       };

       // Act
       var response = await _handler.Handle(request, CancellationToken.None);

       // Assert
       response.Success.Should().BeTrue();
       response.TotalCount.Should().Be(1);
       response.Items.Should().HaveCount(1);
       response.Items[0].ProductCode.Should().Be("SEMI001");
   }
   ```

2. Run only this test:

   ```
   dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
       --filter "FullyQualifiedName~Handle_ExplicitProductType_ReturnsOnlyMatchingType"
   ```

   Expected: `Passed! - 1`

3. Commit:
   ```
   git add backend/test/Anela.Heblo.Tests/Features/Catalog/GetProductMarginsHandlerTests.cs
   git commit -m "test: FR-2 explicit product-type filter returns exact type match only"
   ```
