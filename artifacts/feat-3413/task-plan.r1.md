# Task Plan: Unit Test Coverage for GetProductMarginsHandler — ApplyFilters and ApplySorting

## Goal

Add three unit tests to bring `GetProductMarginsHandler` coverage above the 60% threshold.
Two logic paths are completely uncovered: the default product-type guard in `ApplyFilters`
(else branch when `ProductType` is null) and the catch-all `_` arm in `ApplySorting`
(unknown sort field). No production code is changed.

## Architecture

Pure test-layer addition. One file is modified. The existing `BuildAggregate` helper is
extended with optional parameters so new tests can vary `Type` and omit `monthlyKeys`
without breaking the two existing callers.

Key constraints:
- Every test must configure `TimeProvider` mock with a fixed UTC time — `MapToMarginDto`
  calls `_timeProvider.GetUtcNow()` unconditionally and throws `ArgumentOutOfRangeException`
  if the mock returns a default/zero value.
- The existing tests already set up `_timeProviderMock` in the constructor; the new tests
  share the same mock and must configure it in their Arrange section.

## Tech Stack

- .NET 8, xUnit, Moq, FluentAssertions
- All three are already referenced in the test project — no new NuGet packages needed

## File Map

| Status | File |
|--------|------|
| Modified | `backend/test/Anela.Heblo.Tests/Features/Catalog/GetProductMarginsHandlerTests.cs` |

---

## Tasks

### task: extend-build-aggregate-helper

Extend the private `BuildAggregate` helper so the three new tests can pass a `ProductType`
and omit `monthlyKeys`. The two existing callers pass `monthlyKeys` positionally, so adding
optional parameters at the end preserves them.

**Files:** `backend/test/Anela.Heblo.Tests/Features/Catalog/GetProductMarginsHandlerTests.cs`

1. Open the file. Locate the current `BuildAggregate` signature at line 100:

   ```csharp
   private static CatalogAggregate BuildAggregate(string productCode, IEnumerable<DateTime> monthlyKeys)
   ```

2. Replace the signature and body with:

   ```csharp
   private static CatalogAggregate BuildAggregate(
       string productCode,
       IEnumerable<DateTime>? monthlyKeys = null,
       ProductType type = ProductType.Product)
   {
       var aggregate = new CatalogAggregate
       {
           Id = productCode,
           ProductName = "Test Product",
           Type = type
       };

       foreach (var key in monthlyKeys ?? Enumerable.Empty<DateTime>())
       {
           aggregate.Margins.MonthlyData[key] = new MarginData();
       }

       return aggregate;
   }
   ```

3. The two existing callers pass `monthlyKeys` as the second positional argument — they
   continue to compile without modification because the parameter name and position are
   unchanged. Verify by checking both call sites in the same file (lines ~40 and ~79):
   - `BuildAggregate(productCode: "TEST001", monthlyKeys: new[] { ... })`
   - `BuildAggregate(productCode: "TEST002", monthlyKeys: new[] { ... })`
   Both use named arguments, so they are unaffected.

4. Build the test project to confirm no compilation errors:

   ```
   dotnet build backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj
   ```

   Expected: `Build succeeded.`

5. Commit:
   ```
   git add backend/test/Anela.Heblo.Tests/Features/Catalog/GetProductMarginsHandlerTests.cs
   git commit -m "test: extend BuildAggregate helper with optional type and monthlyKeys params"
   ```

---

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

---

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

---

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
           ProductType = ProductType.Product,   // explicit type so all three items pass the filter
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

   **Why `ProductType = ProductType.Product` here:** the three test items all default to
   `ProductType.Product` (set by `BuildAggregate`). Without setting an explicit type on the
   request, the null default filter passes `Product` and `Goods` anyway, so both work — but
   setting it explicitly makes the test intent self-documenting and immune to future changes
   to `BuildAggregate`'s default type.

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

---

### task: verify-all-tests-pass

Run the full test class to confirm all five tests pass (two pre-existing + three new) and no
regressions were introduced.

**Files:** none (verification only)

1. Run all tests in the handler test class:

   ```
   dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
       --filter "FullyQualifiedName~GetProductMarginsHandlerTests"
   ```

   Expected output:
   ```
   Passed! - Failed: 0, Passed: 5, Skipped: 0, Total: 5
   ```

2. Run the full test suite to confirm nothing else broke:

   ```
   dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj
   ```

   Expected: all tests pass, zero failures.

3. Optionally verify coverage improvement (requires the `coverlet` tooling already configured
   in the project):

   ```
   dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
       --collect:"XPlat Code Coverage" \
       --results-directory ./coverage
   ```

   Check that the line coverage for
   `Anela.Heblo.Application/Features/Catalog/UseCases/GetProductMargins/GetProductMarginsHandler.cs`
   exceeds 60%.
