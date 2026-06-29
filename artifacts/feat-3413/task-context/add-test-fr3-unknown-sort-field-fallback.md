### task: add-test-fr3-unknown-sort-field-fallback

Add a test for FR-3: an unrecognised `SortBy` value silently falls back to `ProductCode`
ascending.

**Files:** `backend/test/Anela.Heblo.Tests/Features/Catalog/GetProductMarginsHandlerTests.cs`

1. Add the following test method after the FR-2 test, before `BuildAggregate`:

   ```csharp
   [Fact]
   public async Task Handle_UnknownSortField_FallsBackToProductCodeAscending()
   {
       // Arrange
       _timeProviderMock
           .Setup(tp => tp.GetUtcNow())
           .Returns(new DateTimeOffset(2026, 6, 29, 12, 0, 0, TimeSpan.Zero));

       var catalogItems = new[]
       {
           BuildAggregate(productCode: "B001"),
           BuildAggregate(productCode: "A001"),
           BuildAggregate(productCode: "C001"),
       };

       _catalogRepositoryMock
           .Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
           .ReturnsAsync(catalogItems);

       var request = new GetProductMarginsRequest
       {
           ProductType = ProductType.Product,
           SortBy = "nonexistent",
           SortDescending = false,
           PageNumber = 1,
           PageSize = 100
       };

       // Act
       var response = await _handler.Handle(request, CancellationToken.None);

       // Assert
       response.Success.Should().BeTrue();
       response.TotalCount.Should().Be(3);
       response.Items.Select(i => i.ProductCode)
           .Should().BeInAscendingOrder();
       response.Items[0].ProductCode.Should().Be("A001");
       response.Items[1].ProductCode.Should().Be("B001");
       response.Items[2].ProductCode.Should().Be("C001");
   }
   ```

2. Run only this test:

   ```
   dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
       --filter "FullyQualifiedName~Handle_UnknownSortField_FallsBackToProductCodeAscending"
   ```

   Expected: `Passed! - 1`

3. Commit:
   ```
   git add backend/test/Anela.Heblo.Tests/Features/Catalog/GetProductMarginsHandlerTests.cs
   git commit -m "test: FR-3 unknown sort field falls back to ProductCode ascending"
   ```
