# Task Plan: Unit Test Coverage for GetMaterialsForPurchaseHandler

**Goal:** Add a focused xUnit/Moq/FluentAssertions test suite for `GetMaterialsForPurchaseHandler` covering the search-term filter, no-purchase-history price fallback, and Take(Limit)-after-filter behavior, raising its line coverage from 11.8% to at least 60% with zero production-code changes.
**Architecture:** Test-only addition to the existing vertical slice `backend/src/Anela.Heblo.Application/Features/Catalog/UseCases/GetMaterialForPurchase/GetMaterialsForPurchaseHandler.cs`. A single new test class mocks `ICatalogRepository.FindAsync` (via `It.IsAny<Expression<Func<CatalogAggregate, bool>>>()`), invokes `Handle` directly, and asserts on the returned `GetMaterialsForPurchaseResponse.Materials`, mirroring the existing `GetProductUsageHandlerTests.cs` pattern in the same directory.
**Tech Stack:** .NET 8, xUnit, Moq, FluentAssertions (all already referenced by `Anela.Heblo.Tests.csproj`).

### task: add-materials-for-purchase-handler-tests

**Files:**
- Create: `backend/test/Anela.Heblo.Tests/Features/Catalog/GetMaterialsForPurchaseHandlerTests.cs` (confirmed via search: no such file exists today; sibling `backend/test/Anela.Heblo.Tests/Features/Catalog/GetProductUsageHandlerTests.cs` is the convention to follow, namespace `Anela.Heblo.Tests.Features.Catalog`)

**Reference — production code under test (unmodified):**
`backend/src/Anela.Heblo.Application/Features/Catalog/UseCases/GetMaterialForPurchase/GetMaterialsForPurchaseHandler.cs`:
```csharp
public async Task<GetMaterialsForPurchaseResponse> Handle(GetMaterialsForPurchaseRequest request, CancellationToken cancellationToken)
{
    var catalogItems = await _catalogRepository.FindAsync(
        item => item.Type == ProductType.Material || item.Type == ProductType.Goods,
        cancellationToken);

    if (!string.IsNullOrWhiteSpace(request.SearchTerm))
    {
        var searchTerm = request.SearchTerm.ToLowerInvariant();
        catalogItems = catalogItems.Where(item =>
            item.ProductCode.ToLowerInvariant().Contains(searchTerm) ||
            item.ProductName.ToLowerInvariant().Contains(searchTerm));
    }

    var materials = catalogItems
        .Take(request.Limit)
        .Select(item => new MaterialForPurchaseDto
        {
            ProductCode = item.ProductCode,
            ProductName = item.ProductName,
            ProductType = item.Type.ToString(),
            LastPurchasePrice = item.PurchaseHistory.LastOrDefault()?.PricePerPiece,
            Location = string.IsNullOrEmpty(item.Location) ? null : item.Location,
            CurrentStock = (int)item.Stock.Available,
            MinimalOrderQuantity = string.IsNullOrEmpty(item.MinimalOrderQuantity) ? null : item.MinimalOrderQuantity
        })
        .OrderBy(m => m.ProductName)
        .ToList();

    return new GetMaterialsForPurchaseResponse { Materials = materials };
}
```
`ICatalogRepository.FindAsync` signature (from `backend/src/Anela.Heblo.Xcc/Persistance/IReadOnlyRepository.cs`): `Task<IEnumerable<TEntity>> FindAsync(Expression<Func<TEntity, bool>> predicate, CancellationToken cancellationToken = default)`.
`CatalogAggregate` relevant members (from `backend/src/Anela.Heblo.Domain/Features/Catalog/CatalogAggregate.cs`): `ProductCode` (string, aliases `Id`), `ProductName` (string), `Type` (`ProductType`, plain settable), `Stock` (`StockData`, plain settable), `Location` (string, default `""`), `PurchaseHistory` (`IReadOnlyList<CatalogPurchaseRecord>`, plain public setter — confirmed, no special mutation API needed), `MinimalOrderQuantity` (string, default `""`).
`ProductType` enum (from `backend/src/Anela.Heblo.Domain/Features/Catalog/ProductType.cs`): `Product = 8, Goods = 1, Material = 3, SemiProduct = 7, Set = 99, UNDEFINED = 0`.
`StockData.Available` (from `backend/src/Anela.Heblo.Domain/Features/Catalog/Stock/StockData.cs`) `=> (PrimaryStockSource == StockSource.Erp ? Erp : Eshop) + Transport + Manufactured`; default `PrimaryStockSource` is `Erp`, so setting `Erp` alone drives `Available`.
`CatalogPurchaseRecord` (from `backend/src/Anela.Heblo.Domain/Features/Catalog/PurchaseHistory/CatalogPurchaseRecord.cs`): `SupplierId, SupplierName, Date, Amount, PricePerPiece, PriceTotal, ProductCode, DocumentNumber`.

- [ ] Step 1: Create the test file with usings, class skeleton, constructor, the `CreateCatalogItem` fixture helper, and the first search-filter test (FR-2, ProductCode-only match). This establishes the pattern the remaining steps extend.

  Write `backend/test/Anela.Heblo.Tests/Features/Catalog/GetMaterialsForPurchaseHandlerTests.cs`:
  ```csharp
  using System.Linq.Expressions;
  using Anela.Heblo.Application.Features.Catalog.UseCases.GetMaterialForPurchase;
  using Anela.Heblo.Domain.Features.Catalog;
  using Anela.Heblo.Domain.Features.Catalog.PurchaseHistory;
  using Anela.Heblo.Domain.Features.Catalog.Stock;
  using FluentAssertions;
  using Moq;
  using Xunit;

  namespace Anela.Heblo.Tests.Features.Catalog;

  public class GetMaterialsForPurchaseHandlerTests
  {
      private readonly Mock<ICatalogRepository> _catalogRepositoryMock;
      private readonly GetMaterialsForPurchaseHandler _handler;

      public GetMaterialsForPurchaseHandlerTests()
      {
          _catalogRepositoryMock = new Mock<ICatalogRepository>();
          _handler = new GetMaterialsForPurchaseHandler(_catalogRepositoryMock.Object);
      }

      [Fact]
      public async Task Handle_SearchTermMatchesProductCodeOnly_ReturnsMatchingItem()
      {
          // Arrange
          var matching = CreateCatalogItem("MAT-ABC123", "Wax");
          var nonMatching = CreateCatalogItem("MAT-999", "Oil");
          var fixtures = new List<CatalogAggregate> { matching, nonMatching };

          _catalogRepositoryMock
              .Setup(x => x.FindAsync(It.IsAny<Expression<Func<CatalogAggregate, bool>>>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync(fixtures);

          var request = new GetMaterialsForPurchaseRequest { SearchTerm = "ABC123", Limit = 50 };

          // Act
          var result = await _handler.Handle(request, CancellationToken.None);

          // Assert
          result.Materials.Should().ContainSingle();
          result.Materials.Single().ProductCode.Should().Be("MAT-ABC123");
      }

      private static CatalogAggregate CreateCatalogItem(
          string productCode,
          string productName,
          ProductType type = ProductType.Material,
          decimal availableStock = 10m,
          string location = "",
          List<CatalogPurchaseRecord>? purchaseHistory = null,
          string minimalOrderQuantity = "")
      {
          return new CatalogAggregate
          {
              ProductCode = productCode,
              ProductName = productName,
              Type = type,
              Stock = new StockData { Erp = availableStock },
              Location = location,
              PurchaseHistory = purchaseHistory ?? new List<CatalogPurchaseRecord>(),
              MinimalOrderQuantity = minimalOrderQuantity
          };
      }
  }
  ```

  Run:
  ```bash
  cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~GetMaterialsForPurchaseHandlerTests"
  ```
  Expect: 1 test passes (the handler already implements this behavior correctly — this is coverage-only, not bug-fixing, so green on first run is expected; if it fails, stop and investigate before continuing).

  Commit:
  ```bash
  git add backend/test/Anela.Heblo.Tests/Features/Catalog/GetMaterialsForPurchaseHandlerTests.cs
  git commit -m "test(catalog): add initial search-filter coverage for GetMaterialsForPurchaseHandler"
  ```

- [ ] Step 2: Add the remaining FR-2 search-term filter tests (ProductName-only match, both-fields match, case-insensitivity, no-match exclusion, and null/empty/whitespace search term via `[Theory]`). Using the Edit tool, insert the following five test methods immediately before the `private static CatalogAggregate CreateCatalogItem(` line (i.e. anchor on that exact line and prepend this block to it):

  ```csharp
      [Fact]
      public async Task Handle_SearchTermMatchesProductNameOnly_ReturnsMatchingItem()
      {
          // Arrange
          var matching = CreateCatalogItem("MAT-001", "Beeswax Premium");
          var nonMatching = CreateCatalogItem("MAT-002", "Oil");
          var fixtures = new List<CatalogAggregate> { matching, nonMatching };

          _catalogRepositoryMock
              .Setup(x => x.FindAsync(It.IsAny<Expression<Func<CatalogAggregate, bool>>>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync(fixtures);

          var request = new GetMaterialsForPurchaseRequest { SearchTerm = "Beeswax", Limit = 50 };

          // Act
          var result = await _handler.Handle(request, CancellationToken.None);

          // Assert
          result.Materials.Should().ContainSingle();
          result.Materials.Single().ProductCode.Should().Be("MAT-001");
      }

      [Fact]
      public async Task Handle_SearchTermMatchesBothFields_ReturnsItemExactlyOnce()
      {
          // Arrange
          var matching = CreateCatalogItem("MAT-WAX01", "Beeswax WAX01");
          var fixtures = new List<CatalogAggregate> { matching };

          _catalogRepositoryMock
              .Setup(x => x.FindAsync(It.IsAny<Expression<Func<CatalogAggregate, bool>>>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync(fixtures);

          var request = new GetMaterialsForPurchaseRequest { SearchTerm = "WAX01", Limit = 50 };

          // Act
          var result = await _handler.Handle(request, CancellationToken.None);

          // Assert
          result.Materials.Should().HaveCount(1);
          result.Materials.Single().ProductCode.Should().Be("MAT-WAX01");
      }

      [Fact]
      public async Task Handle_SearchTermDifferentCase_StillMatches()
      {
          // Arrange
          var matching = CreateCatalogItem("mat-oil01", "olive oil");
          var fixtures = new List<CatalogAggregate> { matching };

          _catalogRepositoryMock
              .Setup(x => x.FindAsync(It.IsAny<Expression<Func<CatalogAggregate, bool>>>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync(fixtures);

          var request = new GetMaterialsForPurchaseRequest { SearchTerm = "OLIVE", Limit = 50 };

          // Act
          var result = await _handler.Handle(request, CancellationToken.None);

          // Assert
          result.Materials.Should().ContainSingle();
          result.Materials.Single().ProductCode.Should().Be("mat-oil01");
      }

      [Fact]
      public async Task Handle_SearchTermMatchesNeitherField_ExcludesItem()
      {
          // Arrange
          var nonMatching = CreateCatalogItem("MAT-999", "Oil");
          var fixtures = new List<CatalogAggregate> { nonMatching };

          _catalogRepositoryMock
              .Setup(x => x.FindAsync(It.IsAny<Expression<Func<CatalogAggregate, bool>>>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync(fixtures);

          var request = new GetMaterialsForPurchaseRequest { SearchTerm = "ZZZNOTFOUND", Limit = 50 };

          // Act
          var result = await _handler.Handle(request, CancellationToken.None);

          // Assert
          result.Materials.Should().BeEmpty();
      }

      [Theory]
      [InlineData(null)]
      [InlineData("")]
      [InlineData("   ")]
      public async Task Handle_NullEmptyOrWhitespaceSearchTerm_ReturnsAllEligibleItems(string? searchTerm)
      {
          // Arrange
          var fixtures = new List<CatalogAggregate>
          {
              CreateCatalogItem("MAT-001", "Beeswax"),
              CreateCatalogItem("MAT-002", "Olive Oil"),
              CreateCatalogItem("MAT-003", "Shea Butter"),
          };

          _catalogRepositoryMock
              .Setup(x => x.FindAsync(It.IsAny<Expression<Func<CatalogAggregate, bool>>>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync(fixtures);

          var request = new GetMaterialsForPurchaseRequest { SearchTerm = searchTerm, Limit = 50 };

          // Act
          var result = await _handler.Handle(request, CancellationToken.None);

          // Assert
          result.Materials.Should().HaveCount(3);
      }

  ```

  Run:
  ```bash
  cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~GetMaterialsForPurchaseHandlerTests"
  ```
  Expect: 8 tests pass total (1 from Step 1 + 5 Facts + 3 Theory cases).

  Commit:
  ```bash
  git add backend/test/Anela.Heblo.Tests/Features/Catalog/GetMaterialsForPurchaseHandlerTests.cs
  git commit -m "test(catalog): cover remaining FR-2 search-filter cases for GetMaterialsForPurchaseHandler"
  ```

- [ ] Step 3: Add the FR-3 no-purchase-history price fallback tests. Using the Edit tool, insert the following two test methods immediately before the `private static CatalogAggregate CreateCatalogItem(` line:

  ```csharp
      [Fact]
      public async Task Handle_NoPurchaseHistory_ReturnsNullLastPurchasePrice()
      {
          // Arrange
          var item = CreateCatalogItem("MAT-001", "Beeswax", purchaseHistory: new List<CatalogPurchaseRecord>());
          var fixtures = new List<CatalogAggregate> { item };

          _catalogRepositoryMock
              .Setup(x => x.FindAsync(It.IsAny<Expression<Func<CatalogAggregate, bool>>>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync(fixtures);

          var request = new GetMaterialsForPurchaseRequest { Limit = 50 };

          // Act
          var result = await _handler.Handle(request, CancellationToken.None);

          // Assert
          result.Materials.Should().ContainSingle();
          result.Materials.Single().LastPurchasePrice.Should().BeNull();
      }

      [Fact]
      public async Task Handle_MultiplePurchaseHistoryRecords_ReturnsLastRecordPrice()
      {
          // Arrange
          var purchaseHistory = new List<CatalogPurchaseRecord>
          {
              new CatalogPurchaseRecord { Date = new DateTime(2024, 1, 1), PricePerPiece = 100m, Amount = 1, ProductCode = "MAT-001", SupplierName = "Supplier A" },
              new CatalogPurchaseRecord { Date = new DateTime(2024, 6, 1), PricePerPiece = 150m, Amount = 1, ProductCode = "MAT-001", SupplierName = "Supplier B" },
          };
          var item = CreateCatalogItem("MAT-001", "Beeswax", purchaseHistory: purchaseHistory);
          var fixtures = new List<CatalogAggregate> { item };

          _catalogRepositoryMock
              .Setup(x => x.FindAsync(It.IsAny<Expression<Func<CatalogAggregate, bool>>>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync(fixtures);

          var request = new GetMaterialsForPurchaseRequest { Limit = 50 };

          // Act
          var result = await _handler.Handle(request, CancellationToken.None);

          // Assert
          result.Materials.Should().ContainSingle();
          result.Materials.Single().LastPurchasePrice.Should().Be(150m);
      }

  ```

  Run:
  ```bash
  cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~GetMaterialsForPurchaseHandlerTests"
  ```
  Expect: 10 tests pass total.

  Commit:
  ```bash
  git add backend/test/Anela.Heblo.Tests/Features/Catalog/GetMaterialsForPurchaseHandlerTests.cs
  git commit -m "test(catalog): cover FR-3 no-purchase-history price fallback for GetMaterialsForPurchaseHandler"
  ```

- [ ] Step 4: Add the FR-4 Take(Limit)-after-filter tests (limit exceeds matches, limit truncates matches, no-search-term truncation, and final ProductName ordering). Using the Edit tool, insert the following four test methods immediately before the `private static CatalogAggregate CreateCatalogItem(` line:

  ```csharp
      [Fact]
      public async Task Handle_LimitExceedsMatchCount_ReturnsAllMatchingItems()
      {
          // Arrange
          var fixtures = new List<CatalogAggregate>
          {
              CreateCatalogItem("MAT-001", "Beeswax Special"),
              CreateCatalogItem("MAT-002", "Beeswax Regular"),
              CreateCatalogItem("MAT-003", "Olive Oil"),
          };

          _catalogRepositoryMock
              .Setup(x => x.FindAsync(It.IsAny<Expression<Func<CatalogAggregate, bool>>>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync(fixtures);

          var request = new GetMaterialsForPurchaseRequest { SearchTerm = "Beeswax", Limit = 50 };

          // Act
          var result = await _handler.Handle(request, CancellationToken.None);

          // Assert
          result.Materials.Should().HaveCount(2);
          result.Materials.Select(m => m.ProductCode).Should().BeEquivalentTo("MAT-001", "MAT-002");
      }

      [Fact]
      public async Task Handle_LimitBelowMatchCount_ReturnsExactlyLimitMatchingItems()
      {
          // Arrange
          var fixtures = new List<CatalogAggregate>
          {
              CreateCatalogItem("MAT-001", "Beeswax A"),
              CreateCatalogItem("MAT-002", "Beeswax B"),
              CreateCatalogItem("MAT-003", "Beeswax C"),
              CreateCatalogItem("MAT-004", "Olive Oil"),
          };

          _catalogRepositoryMock
              .Setup(x => x.FindAsync(It.IsAny<Expression<Func<CatalogAggregate, bool>>>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync(fixtures);

          var request = new GetMaterialsForPurchaseRequest { SearchTerm = "Beeswax", Limit = 2 };

          // Act
          var result = await _handler.Handle(request, CancellationToken.None);

          // Assert — exactly Limit items, and every one of them is a genuine match
          // (proves truncation happens on the filtered set, not before filtering)
          result.Materials.Should().HaveCount(2);
          result.Materials.Select(m => m.ProductCode)
              .Should().OnlyContain(code => new[] { "MAT-001", "MAT-002", "MAT-003" }.Contains(code));
      }

      [Fact]
      public async Task Handle_NoSearchTermWithLimit_ReturnsExactlyLimitItems()
      {
          // Arrange
          var fixtures = new List<CatalogAggregate>
          {
              CreateCatalogItem("MAT-001", "Item A"),
              CreateCatalogItem("MAT-002", "Item B"),
              CreateCatalogItem("MAT-003", "Item C"),
          };

          _catalogRepositoryMock
              .Setup(x => x.FindAsync(It.IsAny<Expression<Func<CatalogAggregate, bool>>>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync(fixtures);

          var request = new GetMaterialsForPurchaseRequest { SearchTerm = null, Limit = 2 };

          // Act
          var result = await _handler.Handle(request, CancellationToken.None);

          // Assert
          result.Materials.Should().HaveCount(2);
      }

      [Fact]
      public async Task Handle_ResultsOrderedByProductNameAlphabetically()
      {
          // Arrange
          var fixtures = new List<CatalogAggregate>
          {
              CreateCatalogItem("MAT-001", "Zinc Oxide"),
              CreateCatalogItem("MAT-002", "Almond Oil"),
              CreateCatalogItem("MAT-003", "Mango Butter"),
          };

          _catalogRepositoryMock
              .Setup(x => x.FindAsync(It.IsAny<Expression<Func<CatalogAggregate, bool>>>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync(fixtures);

          var request = new GetMaterialsForPurchaseRequest { Limit = 50 };

          // Act
          var result = await _handler.Handle(request, CancellationToken.None);

          // Assert
          result.Materials.Select(m => m.ProductName).Should().ContainInOrder("Almond Oil", "Mango Butter", "Zinc Oxide");
      }

  ```

  Run:
  ```bash
  cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~GetMaterialsForPurchaseHandlerTests"
  ```
  Expect: 14 tests pass total.

  Commit:
  ```bash
  git add backend/test/Anela.Heblo.Tests/Features/Catalog/GetMaterialsForPurchaseHandlerTests.cs
  git commit -m "test(catalog): cover FR-4 filter-then-limit and ordering for GetMaterialsForPurchaseHandler"
  ```

- [ ] Step 5: Add the FR-5 field-mapping tests (ProductType string mapping; empty vs. non-empty Location/MinimalOrderQuantity; incidental CurrentStock cast). Using the Edit tool, insert the following three test methods immediately before the `private static CatalogAggregate CreateCatalogItem(` line:

  ```csharp
      [Fact]
      public async Task Handle_MapsProductTypeToStringRepresentation()
      {
          // Arrange
          var fixtures = new List<CatalogAggregate>
          {
              CreateCatalogItem("MAT-001", "Beeswax", type: ProductType.Material),
              CreateCatalogItem("GDS-001", "Gift Box", type: ProductType.Goods),
          };

          _catalogRepositoryMock
              .Setup(x => x.FindAsync(It.IsAny<Expression<Func<CatalogAggregate, bool>>>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync(fixtures);

          var request = new GetMaterialsForPurchaseRequest { Limit = 50 };

          // Act
          var result = await _handler.Handle(request, CancellationToken.None);

          // Assert
          result.Materials.Single(m => m.ProductCode == "MAT-001").ProductType.Should().Be("Material");
          result.Materials.Single(m => m.ProductCode == "GDS-001").ProductType.Should().Be("Goods");
      }

      [Fact]
      public async Task Handle_EmptyLocationAndMinimalOrderQuantity_MapToNull()
      {
          // Arrange
          var item = CreateCatalogItem("MAT-001", "Beeswax", location: "", minimalOrderQuantity: "");
          var fixtures = new List<CatalogAggregate> { item };

          _catalogRepositoryMock
              .Setup(x => x.FindAsync(It.IsAny<Expression<Func<CatalogAggregate, bool>>>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync(fixtures);

          var request = new GetMaterialsForPurchaseRequest { Limit = 50 };

          // Act
          var result = await _handler.Handle(request, CancellationToken.None);

          // Assert
          var dto = result.Materials.Single();
          dto.Location.Should().BeNull();
          dto.MinimalOrderQuantity.Should().BeNull();
      }

      [Fact]
      public async Task Handle_NonEmptyLocationAndMinimalOrderQuantity_PassThroughUnchangedWithStock()
      {
          // Arrange
          var item = CreateCatalogItem("MAT-001", "Beeswax", availableStock: 42m, location: "A-12-3", minimalOrderQuantity: "10 kg");
          var fixtures = new List<CatalogAggregate> { item };

          _catalogRepositoryMock
              .Setup(x => x.FindAsync(It.IsAny<Expression<Func<CatalogAggregate, bool>>>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync(fixtures);

          var request = new GetMaterialsForPurchaseRequest { Limit = 50 };

          // Act
          var result = await _handler.Handle(request, CancellationToken.None);

          // Assert
          var dto = result.Materials.Single();
          dto.Location.Should().Be("A-12-3");
          dto.MinimalOrderQuantity.Should().Be("10 kg");
          dto.CurrentStock.Should().Be(42);
      }

  ```

  Run:
  ```bash
  cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~GetMaterialsForPurchaseHandlerTests"
  ```
  Expect: 17 tests pass total.

  Commit:
  ```bash
  git add backend/test/Anela.Heblo.Tests/Features/Catalog/GetMaterialsForPurchaseHandlerTests.cs
  git commit -m "test(catalog): cover FR-5 field mapping for GetMaterialsForPurchaseHandler"
  ```

- [ ] Step 6: Full validation pass — run the complete backend test suite (not just this filter) to confirm no regressions, verify coverage on the handler meets the 60% threshold, and check formatting/build per project standard.

  Run:
  ```bash
  cd backend && dotnet build
  cd backend && dotnet format --verify-no-changes
  cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj
  ```
  If `dotnet format --verify-no-changes` reports issues, run `dotnet format` (no `--verify-no-changes`) to fix them, then re-run `dotnet build` and the test suite.

  If a coverage report is part of this repo's standard CI check (see `docs/architecture/testing-strategy.md` for the exact command), run it scoped to the `Anela.Heblo.Tests` project and confirm `GetMaterialsForPurchaseHandler.cs` line coverage is ≥60%. If any FR-2–FR-5 branch is still uncovered, add the missing case following the same pattern as Steps 1–5 before proceeding.

  Commit (only if `dotnet format` made changes):
  ```bash
  git add backend/test/Anela.Heblo.Tests/Features/Catalog/GetMaterialsForPurchaseHandlerTests.cs
  git commit -m "chore(catalog): apply dotnet format to GetMaterialsForPurchaseHandlerTests"
  ```
